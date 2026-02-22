#!/usr/bin/env python3
"""
Stop hook - fires when Claude finishes responding.
Logs the stop event. Does not block.
"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hook_base import run_hook, format_log_only_output

if __name__ == "__main__":
    run_hook("Stop", format_log_only_output)
