@echo off
set "TEMP=C:\Windows\Temp"
set "TMP=C:\Windows\Temp"
set "VTK_DEFAULT_OPENGL_WINDOW=vtkWin32OpenGLRenderWindow"
cd /d "D:\Office\AxialFanMVC.Business\Cfd\Render"
echo ===== RUN %DATE% %TIME% ===== >> "D:\Office\CfdIpc\dispatch.log"
"C:\Users\Admin\AppData\Local\Python\pythoncore-3.14-64\python.exe" "D:\Office\AxialFanMVC.Business\Cfd\Render\render_dispatch.py" "D:\Office\CfdIpc" >> "D:\Office\CfdIpc\dispatch.log" 2>&1
echo EXITCODE %ERRORLEVEL% >> "D:\Office\CfdIpc\dispatch.log"
