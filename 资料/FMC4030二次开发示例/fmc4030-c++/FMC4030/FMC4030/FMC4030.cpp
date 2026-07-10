#include <iostream>
#include <stdio.h>
#include "FMC4030-Dll.h"
#include <Windows.h>
#include <string>

int main()
{
	char ip_addr[30] = { 0 };
	float pos = 0;
	float speed = 0;
	int status = 0;
	int input = 0;
	char fileName[30];

	sprintf_s(ip_addr, "%s", "192.168.0.30");
	std::cout << "open device:" << FMC4030_Open_Device(0, ip_addr, 8088) << "\r\n";
	std::cout << "jog return:" << FMC4030_Jog_Single_Axis(0, 0, 100, 50, 200, 200, 1) << "\r\n";
	Sleep(100);
	while ((!FMC4030_Check_Axis_Is_Stop(0, 0)))
	{
		Sleep(1000);
		std::cout << "get pos status:" << FMC4030_Get_Axis_Current_Pos(0, 0, &pos) << "\r\n";
		std::cout << "get speed status:" << FMC4030_Get_Axis_Current_Speed(0, 0, &speed) << "\r\n";
		std::cout << "x pos:" << pos << "\r\n";
		std::cout << "x speed:" << speed << "\r\n";
		std::cout << "machineRunStatus :" << FMC4030_Get_machineRunStatus(0) << "\r\n";
	}
	std::cout << "machineRunStatus :" << FMC4030_Get_machineRunStatus(0) << "\r\n";


	//std::cout << "home return:" << FMC4030_Home_Single_Axis(0, 0, 50, 200, 5, 1) << "\r\n";
	//Sleep(100);
	//while (FMC4030_Get_axisStatus(0, 0, MACHINE_HOME))
	//{
	//	Sleep(1000);
	//	std::cout << "FMC4030_Get_axis_all_Status return:" << FMC4030_Get_axis_all_Status(0, 0, &status) << "\r\n";
	//	printf("axis 0 status is:0X%X\r\n", status);

	//}

	//int flag = 0;
	//while (1)
	//{
	//	for (int i = 0; i < 4; i++)
	//	{
	//		Sleep(1000);
	//		FMC4030_Get_Input(0, i, &input);
	//		printf("input io :%d inputStatus is: %d\r\n", i, input);
	//		if (i == 0 && input == 0)
	//		{
	//			std::cout << "input 0 is:" << input << "\r\n";
	//			flag = 1;
	//			break;
	//		}
	//	}
	//	if (flag == 1) break;
	//}
	
	//for (int i = 1; i <= 20; i++)
	//{
	//	memset(fileName, 0, sizeof(fileName));
	//	if (FMC4030_Get_Auto_File(0, i, fileName) == 0)
	//	{
	//		printf("FILE %d, file name is %s\r\n", i, fileName);
	//	}
	//}
	

	std::cout << "close device:" << FMC4030_Close_Device(0) << "\r\n";
}


