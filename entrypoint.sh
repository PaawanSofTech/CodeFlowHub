#!/bin/sh
# Railway sets $PORT dynamically. ASP.NET reads ASPNETCORE_URLS at startup.
export ASPNETCORE_URLS="http://+:${PORT:-5000}"
exec dotnet CodeFlow.API.dll