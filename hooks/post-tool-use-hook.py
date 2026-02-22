#!/usr/bin/env python3
"""
PostToolUse hook - fires AFTER a tool executes successfully.
Can inject additional context but cannot undo the operation.
Used for logging and post-execution validation.
"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hook_base import run_hook, format_post_tool_output

if __name__ == "__main__":
    run_hook("PostToolUse", format_post_tool_output)
