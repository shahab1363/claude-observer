#!/usr/bin/env python3
"""
Shared base module for all Claude Permission Analyzer hook scripts.
Uses only Python standard library (no pip dependencies).
"""
import json
import os
import re
import sys
import socket
from urllib.parse import urlparse
from urllib.request import Request, urlopen
from urllib.error import URLError, HTTPError

SERVICE_URL = os.environ.get("CLAUDE_ANALYZER_URL", "http://localhost:5050/api/analyze")
API_KEY = os.environ.get("CLAUDE_ANALYZER_API_KEY", "")
TIMEOUT = 30
MAX_INPUT_SIZE = 1_000_000  # 1MB
MAX_SESSION_ID_LENGTH = 128
SESSION_ID_PATTERN = re.compile(r'^[a-zA-Z0-9\-_]+$')


def validate_service_url(url: str) -> bool:
    """Only allow localhost/loopback URLs to prevent SSRF."""
    try:
        parsed = urlparse(url)
        if parsed.scheme not in ("http", "https"):
            return False
        host = parsed.hostname or ""
        return host in ("localhost", "127.0.0.1", "::1")
    except Exception:
        return False


def validate_hook_input(data: dict):
    """Validate hook input fields. Returns error message or None."""
    session_id = data.get("session_id", "")
    if session_id:
        if not isinstance(session_id, str):
            return "Invalid session_id type"
        if len(session_id) > MAX_SESSION_ID_LENGTH:
            return "session_id exceeds maximum length"

    tool_name = data.get("tool_name")
    if tool_name is not None:
        if not isinstance(tool_name, str) or len(tool_name) > 256:
            return "Invalid tool_name"
    return None


def read_hook_input() -> dict:
    """Read and validate JSON input from stdin."""
    if not validate_service_url(SERVICE_URL):
        print("ERROR: Service URL must be a localhost/loopback address", file=sys.stderr)
        sys.exit(1)

    raw_input = sys.stdin.read(MAX_INPUT_SIZE + 1)
    if len(raw_input) > MAX_INPUT_SIZE:
        print(f"ERROR: Input exceeds maximum size of {MAX_INPUT_SIZE} bytes", file=sys.stderr)
        sys.exit(1)

    hook_input = json.loads(raw_input)
    if not isinstance(hook_input, dict):
        print("ERROR: Input must be a JSON object", file=sys.stderr)
        sys.exit(1)

    error = validate_hook_input(hook_input)
    if error:
        print(f"ERROR: Input validation failed: {error}", file=sys.stderr)
        sys.exit(1)

    return hook_input


def send_to_analyzer(hook_input: dict) -> dict:
    """Send hook input to the analyzer service using urllib (no dependencies)."""
    data = json.dumps(hook_input).encode("utf-8")
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json",
    }
    if API_KEY:
        headers["X-Api-Key"] = API_KEY

    req = Request(SERVICE_URL, data=data, headers=headers, method="POST")
    resp = urlopen(req, timeout=TIMEOUT)

    if resp.status != 200:
        raise RuntimeError(f"Service error {resp.status}")

    return json.loads(resp.read().decode("utf-8"))


def format_permission_output(result: dict) -> dict:
    """Format output for PermissionRequest hooks."""
    output = {
        "hookSpecificOutput": {
            "hookEventName": "PermissionRequest",
            "decision": {
                "behavior": "allow" if result.get("autoApprove") else "deny"
            }
        }
    }
    if not result.get("autoApprove"):
        reasoning = str(result.get("reasoning", "Denied"))[:1000]
        output["hookSpecificOutput"]["decision"]["message"] = (
            f"Safety score {result.get('safetyScore', 0)} below threshold "
            f"{result.get('threshold', 0)}. Reason: {reasoning}"
        )
        if result.get("interrupt"):
            output["hookSpecificOutput"]["decision"]["interrupt"] = True
    return output


def format_pre_tool_output(result: dict) -> dict:
    """Format output for PreToolUse hooks."""
    score = result.get("safetyScore", 0)
    threshold = result.get("threshold", 85)

    if score >= threshold:
        decision = "allow"
    elif score < 30:
        decision = "deny"
    else:
        decision = "ask"

    output = {
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": decision,
        }
    }
    if decision != "allow":
        output["hookSpecificOutput"]["permissionDecisionReason"] = (
            str(result.get("reasoning", "Requires review"))[:1000]
        )
    return output


def format_post_tool_output(result: dict) -> dict:
    """Format output for PostToolUse hooks."""
    output = {}
    suggestion = result.get("suggestion") if isinstance(result, dict) else None
    if suggestion:
        output["hookSpecificOutput"] = {
            "hookEventName": "PostToolUse",
            "additionalContext": str(suggestion)[:500]
        }
    return output


def format_log_only_output() -> dict:
    """Format output for informational-only hooks (no decision)."""
    return {}


def run_hook(hook_event_name: str, format_fn=None):
    """Main hook runner. Reads input, sends to analyzer, formats output."""
    try:
        hook_input = read_hook_input()
        hook_input["hookEventName"] = hook_event_name

        result = send_to_analyzer(hook_input)

        # If service returned passthrough, output empty JSON (no decision)
        if result.get("passthrough"):
            print("{}")
            sys.exit(0)

        if format_fn:
            output = format_fn(result)
        else:
            output = format_permission_output(result)

        if output:
            print(json.dumps(output))
        sys.exit(0)

    except json.JSONDecodeError as e:
        print(f"ERROR: Invalid JSON: {e}", file=sys.stderr)
        sys.exit(1)
    except socket.timeout:
        print("ERROR: Analyzer service timed out", file=sys.stderr)
        sys.exit(1)
    except ConnectionRefusedError:
        # Service not running - fall through to normal Claude permission prompt
        sys.exit(0)
    except URLError as e:
        if isinstance(e.reason, ConnectionRefusedError):
            # Service not running - fall through
            sys.exit(0)
        print(f"ERROR: Request failed: {e.reason}", file=sys.stderr)
        sys.exit(1)
    except HTTPError as e:
        print(f"ERROR: Service error {e.code}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"ERROR: {type(e).__name__}: {e}", file=sys.stderr)
        sys.exit(1)
