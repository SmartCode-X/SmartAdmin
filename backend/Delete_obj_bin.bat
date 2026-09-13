@echo off
set nowPath=%cd%
cd /
cd %nowPath%

::删除指定后缀名文件(*.pdb,*.vshost.*)
for /r %nowPath% %%i in (*.pdb,*.vshost.*) do (del %%i)

::删除指定文件夹(obj,bin)
for /r %nowPath% %%i in (obj,bin) do (IF EXIST %%i RD /s /q %%i)

::echo OK
::pause
system("pause > nul")