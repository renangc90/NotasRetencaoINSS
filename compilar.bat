@echo off
rem Compila com o csc que ja vem no Windows (.NET Framework 4.8). Nao precisa instalar nada.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set REFS=/r:System.dll /r:System.Core.dll /r:System.Data.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Security.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll
if not exist dist mkdir dist

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32manifest:src\app.manifest %REFS% /out:dist\NotasRetencaoINSS.exe src\*.cs
if errorlevel 1 goto erro

if "%1"=="testes" (
  if not exist testes\bin mkdir testes\bin
  "%CSC%" /nologo /target:exe /codepage:65001 /main:NotasRetencaoINSS.Testes %REFS% /out:testes\bin\Testes.exe src\*.cs testes\*.cs
  if errorlevel 1 goto erro
)
echo Pronto: dist\NotasRetencaoINSS.exe
exit /b 0

:erro
echo FALHOU A COMPILACAO
exit /b 1
