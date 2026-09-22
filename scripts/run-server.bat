@echo off
cd /d "%~dp0..\server"
uv run uvicorn etherbound.app:app --reload
