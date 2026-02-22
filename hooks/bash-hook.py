#!/usr/bin/env python3
"""Bash PermissionRequest hook - uses hook_base for shared logic."""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from hook_base import run_hook, format_permission_output

if __name__ == "__main__":
    run_hook("PermissionRequest", format_permission_output)
