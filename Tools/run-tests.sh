#!/usr/bin/env bash
# Builds the engine-independent simulation (netstandard2.1 / C# 9) and runs its NUnit tests.
# Requires the .NET 8 SDK. Does NOT require Unity.
set -euo pipefail
cd "$(dirname "$0")/../Tests/DotNet/SimulationTests"
dotnet test FFTS.Simulation.Tests.csproj --nologo -v q --logger "console;verbosity=normal" "$@"
