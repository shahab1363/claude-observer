#!/usr/bin/env python3
"""
UserPromptSubmit hook - fires when user submits a prompt.
Logs the prompt and optionally injects context. Never blocks user prompts.
"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hook_base import run_hook, format_log_only_output

if __name__ == "__main__":
    run_hook("UserPromptSubmit", format_log_only_output)
