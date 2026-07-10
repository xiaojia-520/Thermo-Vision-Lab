%Load Library From Local FilePath
loadlibrary('FMC4030-Dll.dll', 'FMC4030-DLL.h')
libisloaded('FMC4030-Dll.dll')

%Connect Device
calllib('FMC4030-Dll.dll', 'FMC4030_Open_Device', 0, '192.168.0.30', 8088)

%Start X-Axis Run Absolute  pos:10mm  speed:20mm/s  acc:200mm/s2  dec:200mm/s2
calllib('FMC4030-Dll.dll', 'FMC4030_Jog_Single_Axis', 0, 0, 10, 20, 200, 200, 1)

%Disconnect Device
calllib('FMC4030-Dll.dll', 'FMC4030_Close_Device', 0)

%Release Library
unloadlibrary('FMC4030-Dll.dll')