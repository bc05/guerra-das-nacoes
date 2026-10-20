#!/usr/bin/env python3
"""Compatibility entry point for the protocol-2 HTTP integration checks.

The original protocol-1 starter catalog is now exercised through legacy migration
fixtures in the C# self-tests; HTTP uses the current instance/wallet/admin contract.
"""
import runpy
from pathlib import Path

if __name__ == '__main__':
    runpy.run_path(str(Path(__file__).with_name('economy_http_checks.py')),run_name='__main__')
