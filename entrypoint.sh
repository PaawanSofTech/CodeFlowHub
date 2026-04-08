#!/bin/sh
# ASP.NET 8 reads HTTP_PORTS automatically when set by Railway.
# We only set ASPNETCORE_URLS if HTTP_PORTS is absent (local Docker, etc.)
if [ -z "$HTTP_PORTS" ] && [ -n "$PORT" ]; then
  export ASPNETCORE_URLS="http://+:${PORT}"
fi
exec dotnet CodeFlow.API.dll