#!/usr/bin/env python3
"""
PostToolUseFailure hook - fires when a tool operation fails.
Logs the failure and optionally injects context about what went wrong.
"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hook_base import run_hook, format_log_only_output

if __name__ == "__main__":
    run_hook("PostToolUseFailure", format_log_only_output)
