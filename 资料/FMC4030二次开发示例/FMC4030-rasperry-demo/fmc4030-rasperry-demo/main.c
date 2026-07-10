#include "FMC4030-Dll.h"
#include "stdio.h"
#include "stdlib.h"
int main()
{
    int res = 0;
    int cnt = 100;
    struct machine_status ms;

    printf("FMC4030-Demo Start\r\n");
    
    res = FMC4030_Open_Device(0, "192.168.0.30", 8088);
    if(res != 0)
    {
        perror("FMC OPEN DEVICE");
        return -1;
    }

    res = FMC4030_Jog_Single_Axis(0, 0, 1000, 100, 100, 200, 2);

    while(!FMC4030_Check_Axis_Is_Stop(0, 0))
    {

        FMC4030_Get_Machine_Status(0, (unsigned char*)&ms);
        printf("axis x pos: %f\r\n", ms.realPos[0]);
    }

    FMC4030_Close_Device(0);

    return 0;
}
