#!/usr/bin/env python3
"""
PreToolUse hook - fires BEFORE a tool executes.
Can allow, deny, or ask for user confirmation.
This is the primary safety gate for all tool operations.
"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hook_base import run_hook, format_pre_tool_output

if __name__ == "__main__":
    run_hook("PreToolUse", format_pre_tool_output)
