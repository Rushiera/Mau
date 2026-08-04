@echo off
echo Building test fixtures...
cd valid
dotnet build FL_ValidFlow.csproj -c Release -o ..\bin\
cd ..\nointerface
dotnet build FL_NoInterface.csproj -c Release -o ..\bin\
cd ..
echo Done.
