import java.util.Arrays;

import static java.lang.Thread.sleep;

public class Main {
    public static void main(String[] args) throws InterruptedException {

        int ret;
        int singleAxis = 0;
        int id = 1;
        float[] pos = new float[1];
        float[] speed = new float[1];
        float[] encoderPos = new float[1];
        float targetPos = 200;
        float targetSpeed = 50;
        float targetACC = 200;
        float targetDec = 200;
        int mode = 1; //1相对运动，2绝对运动


        FMC4030 fmc4030 = new FMC4030();

        ret = fmc4030.FMC4030_Open_Device(id, "192.168.0.30", 8088);
        System.out.println(ret);

        //测试单轴运动
//        for(int i = 0; i < 3; i++)
//        {
//            ret = fmc4030.FMC4030_Jog_Single_Axis(id, singleAxis + i, targetPos, targetSpeed, targetACC, targetDec, mode);
//            System.out.println("单轴" + i + "返回值：" + ret);
//        }
//        int myAxis = singleAxis;
//        while (fmc4030.FMC4030_Check_Axis_Is_Stop(id, myAxis) == 0)
//        {
//            ret = fmc4030.FMC4030_Get_Axis_Current_Pos(id, myAxis, pos);
//            System.out.println("轴：" + myAxis + "位置"+ pos[0] + ",返回：" + ret);
//            ret = fmc4030.FMC4030_Get_Axis_Current_Speed(id, myAxis, speed);
//            System.out.println("轴：" + myAxis + "速度"+ speed[0] + ",返回：" + ret);
//            myAxis += 1;
//            if(myAxis == 3)
//            {
//                myAxis = 0;
//            }
//            sleep(1000);
//        }
        //测试回零运动
        float homeSpeed = 50;
        float homeAccDec = 200;
        float homeFallStep = 10;
        int homeDir = 2; //1正向回零，2，反向回零， 3，当前位置回零

        for(int i = 0; i < 3; i++)
        {
            ret = fmc4030.FMC4030_Home_Single_Axis(id, singleAxis + i, homeSpeed, homeAccDec, homeFallStep, homeDir);
            System.out.println("单轴回零" + i + "返回值：" + ret);
        }
        int homeAxis = 0;
        MachineStatus status = new MachineStatus();
        do {
            ret = fmc4030.FMC4030_Get_Machine_Status(id, status);
            if (ret != 0) continue;

            System.out.println("轴" + homeAxis + "位置：" + status.realPos[homeAxis]);
            System.out.println("轴" + homeAxis + "速度：" + status.realSpeed[homeAxis]);
            System.out.println("轴" + homeAxis + "状态：" + String.format("%04X", status.axisStatus[homeAxis]));
            homeAxis += 1;
            if(homeAxis == 3)
            {
                homeAxis = 0;
            }

            System.out.println("输入IO状态" + String.format("%04X", status.inputStatus));
            System.out.println("输出IO状态" + String.format("%04X", status.outputStatus));
            System.out.println("设备运行状态" + String.format("%04X", status.machineRunStatus));
            for (int i = 0; i < 20; i++)
            {
                System.out.println("文件" + i + ":" + status.file[i]);
            }

            if((status.axisStatus[0] & FMC4030.MACHINE_HOME) == 0
                    && (status.axisStatus[1] & FMC4030.MACHINE_HOME) == 0
                    &&(status.axisStatus[2] & FMC4030.MACHINE_HOME) == 0
                   )
            {
                break;
            }
            sleep(1000);
        }while(true);

        //测试单轴的开始与停止。
//        singleAxis = 0;
//        fmc4030.FMC4030_Jog_Single_Axis(id, singleAxis, 10000, 50, 200, 200, 1); //绝对运动到10000
//        do{
//            ret = fmc4030.FMC4030_Get_Axis_Current_Pos(id, singleAxis, pos);
//            System.out.println("轴：" + singleAxis + "位置"+ pos[0] + ",返回：" + ret);
//
//            if(pos[0] >= 400)
//            {
//                fmc4030.FMC4030_Stop_Single_Axis(id, singleAxis, 1); //1减速定制，2马上停止
//                break;
//            }
//            sleep(100);
//        }while(true);

        //测试两轴直线插补
//        int interAxis = 3;//按位表示，X Y轴组合对应二进制的 011
//        ret = fmc4030.FMC4030_Line_2Axis(id, interAxis, 500, 500, 50, 200, 200);
//        do {
//            ret = fmc4030.FMC4030_Get_Axis_Current_Pos(id, 0, pos);
//            System.out.println("X轴：" + "位置" + pos[0] + ",返回：" + ret);
//            ret = fmc4030.FMC4030_Get_Axis_Current_Pos(id, 1, pos);
//            System.out.println("Y轴：" + "位置" + pos[0] + ",返回：" + ret);
//        } while (fmc4030.FMC4030_Check_Axis_Is_Stop(id, 0) == 0);

        //测试三轴直线插补
//        interAxis = 7;//按位表示，X Y Z轴组合对应二进制的 111
//        ret = fmc4030.FMC4030_Line_3Axis(id, interAxis, 500, 500, 300, 50, 200, 200);
//        do {
//            ret = fmc4030.FMC4030_Get_Axis_Current_Pos(id, 0, pos);
//            System.out.println("X轴：" + "位置" + pos[0] + ",返回：" + ret);
//            ret = fmc4030.FMC4030_Get_Axis_Current_Pos(id, 1, pos);
//            System.out.println("Y轴：" + "位置" + pos[0] + ",返回：" + ret);
//            ret = fmc4030.FMC4030_Get_Axis_Current_Pos(id, 2 , pos);
//            System.out.println("Z轴：" + "位置" + pos[0] + ",返回：" + ret);
//        } while (fmc4030.FMC4030_Check_Axis_Is_Stop(id, 0) == 0);

        //测试两轴圆弧插补
//        interAxis = 3;//按位表示，X1 Y1轴组合对应二进制的 11 000
//        int arcDir = 1;//1顺时针，2，逆时针
//        ret = fmc4030.FMC4030_Arc_2Axis(id, interAxis, 400, 0, 200, 0, 200, 50, 200, 200, arcDir);
//        do {
//            ret = fmc4030.FMC4030_Get_Axis_Current_Pos(id, 0, pos);
//            System.out.println("X轴：" + "位置" + pos[0] + ",返回：" + ret);
//            ret = fmc4030.FMC4030_Get_Axis_Current_Pos(id,1, pos);
//            System.out.println("Y轴：" + "位置" + pos[0] + ",返回：" + ret);
//        } while (fmc4030.FMC4030_Check_Axis_Is_Stop(id, 0) == 0);

        //获取设备参数与设置参数
//        MachineDevicePara devicePara = new MachineDevicePara();
//        ret = fmc4030.FMC4030_Get_Device_Para(id, devicePara);
//        System.out.println("返回：" + ret);
//        System.out.println("id" + ":" + devicePara.id );
//        System.out.println("bound232" + ":" + devicePara.bound232 );
//        System.out.println("bound485" + ":" + devicePara.bound485 );
//        System.out.println("ip" + ":" + devicePara.ip);
//        System.out.println("port" + ":" + devicePara.port );
//
//        for (int i = 0; i < 3; i++)
//        {
//            System.out.println("轴" + i+ "细分:" + devicePara.div[i] );
//            System.out.println("轴" + i+ "导程:" + devicePara.lead[i] );
//            System.out.println("轴" + i+ "正软限位:" + devicePara.softLimitMax[i] );
//            System.out.println("轴" + i+ "负软限位:" + devicePara.softLimitMin[i] );
//            System.out.println("轴" + i+ "回零时间:" + devicePara.homeTime[i] );
//        }
//        devicePara.bound232 = 115200;
//        devicePara.bound485 = 115200;
//        for(int i = 0; i < 3; i++)
//        {
//            devicePara.div[i] = 10000;
//            devicePara.homeTime[i] = 200000;
//            devicePara.softLimitMax[i] = 2000;
//            devicePara.softLimitMin[i] = 2000;
//        }
//        ret = fmc4030.FMC4030_Set_Device_Para(id, devicePara);
//        System.out.println("设置返回：" + ret);
//
//        ret = fmc4030.FMC4030_Get_Device_Para(id, devicePara);
//        System.out.println("再一次获取返回：" + ret);
//        System.out.println("id" + ":" + devicePara.id );
//        System.out.println("bound232" + ":" + devicePara.bound232 );
//        System.out.println("bound485" + ":" + devicePara.bound485 );
//        System.out.println("ip" + ":" + devicePara.ip);
//        System.out.println("port" + ":" + devicePara.port );
//
//        for (int i = 0; i < 3; i++)
//        {
//            System.out.println("轴" + i+ "细分:" + devicePara.div[i] );
//            System.out.println("轴" + i+ "导程:" + devicePara.lead[i] );
//            System.out.println("轴" + i+ "正软限位:" + devicePara.softLimitMax[i] );
//            System.out.println("轴" + i+ "负软限位:" + devicePara.softLimitMin[i] );
//            System.out.println("轴" + i+ "回零时间:" + devicePara.homeTime[i] );
//        }
        //获取版本号
//        MachineVersion machineVersion = new MachineVersion();
//        ret = fmc4030.FMC4030_Get_Version_Info(id, machineVersion);
//        System.out.println("返回：" + ret);
//        System.out.println("firmware:" + machineVersion.firmware );
//        System.out.println("lib:" + machineVersion.lib );
//        System.out.println("serialnumber:" + machineVersion.serialnumber );

        //自动控制相关的
        //获取自动化的相关参数
//        MachineStatus autoStatus = new MachineStatus();
//        ret = fmc4030.FMC4030_Get_Machine_Status(id, autoStatus);
//        System.out.println("返回：" + ret);
//        if (ret == 0)
//        {
//            for(int i = 0; i < 20; i++)
//            {
//                System.out.println("文件" + i + ":" + autoStatus.file[i] );
//            }
//        }
//
//        ret = fmc4030.FMC4030_Start_Auto_Run(id, autoStatus.file[0]);
//        System.out.println("开始自动程序返回：" + ret);
//        sleep(50000);
//
//        ret = fmc4030.FMC4030_Stop_Auto_Run(id);
//        System.out.println("停止自动程序返回：" + ret);

        //脚本文件下载
//        ret = fmc4030.FMC4030_Download_File(id, "/home/lijun/Desktop/19.elo", 2);
//        System.out.println("下载返回：" + ret);
//        sleep(1000);
//        MachineStatus myAutoStatus = new MachineStatus();
//        ret = fmc4030.FMC4030_Get_Machine_Status(id, myAutoStatus);
//        System.out.println("返回：" + ret);
//        if (ret == 0)
//        {
//            for(int i = 0; i < 20; i++)
//            {
//                System.out.println("文件" + i + ":" + myAutoStatus.file[i] );
//            }
//        }
        //文件删除
//        ret = fmc4030.FMC4030_Delete_Script_File(id, "19.elo");
//        System.out.println("删除返回：" + ret);
//       // 固件文件下载
//        ret = fmc4030.FMC4030_Download_File(id, "/home/lijun/Desktop/FMC4030_APP.bin", 1);
//        System.out.println("下载返回：" + ret);

        //测试本地IO输入输出
//        for(int i = 0; i < 4; i++)
//        {
//            ret = fmc4030.FMC4030_Set_Output(id, i, 1);
//            System.out.println("IO输出：" + i + ",返回：" + ret);
//        }
//        int[] inputStatus = new int[1];
//        do {
//            for (int i = 0; i < 4; i++) {
//                ret = fmc4030.FMC4030_Get_Input(id, i, inputStatus);
//                System.out.println("IO输入：" + i + "状态：" + inputStatus[0] + ",返回：" + ret);
//            }
//            fmc4030.FMC4030_Get_Input(id, 0, inputStatus);
//            sleep(1000);
//        } while (inputStatus[0] != 0);

        //获取设备运行状态。
//        ret = fmc4030.FMC4030_Get_machineRunStatus(id);
//        System.out.println("设备运行状态：" + ret);

        //获取当前轴的状态
        /* 各轴状态@machine_status.axisStatus */
   /* #define MACHINE_POWER_ON                   0x0000     //无意义
    #define MACHINE_RUNNING                    0x0001     //轴正在运行
    #define MACHINE_PAUSE                      0x0002     //轴暂停运行
    #define MACHINE_RESUME                     0x0004     //无
    #define MACHINE_STOP                       0x0008     //轴停止运行
    #define MACHINE_LIMIT_N                    0x0010     //负限位触发
    #define MACHINE_LIMIT_P                    0x0020	  //正限位触发
    #define MACHINE_HOME_DONE                  0x0040     //轴回零完成
    #define MACHINE_HOME                       0x0080     //轴回零中
    #define MACHINE_AUTO_RUN                   0x0100     //无
    #define MACHINE_LIMIT_N_NONE               0x0200     //负限位未触发
    #define MACHINE_LIMIT_P_NONE               0x0400     //正限位未触发
    #define MACHINE_HOME_NONE                  0x0800     //未回零
    #define MACHINE_HOME_OVERTIME              0x1000     //回零超时
    */
//        ret = fmc4030.FMC4030_Get_axisStatus(id, 0, 0x0040);
//        System.out.println("轴当前查询状态返回：" + ret);
//        int[] axisStatus = new int[1];
//        ret = fmc4030.FMC4030_Get_axis_all_Status(id, 0, axisStatus);
//        System.out.println("轴所有状态返回：" + ret);
//        System.out.println("轴所有状态：" + String.format("%02X", axisStatus[0]));
        //获取
//        char[] fileName = new char[30];
//        for(int i = 1; i <= 20; i++) {
//            // 每次循环前清空数组（关键修复）
//            Arrays.fill(fileName, '\0');
//
//            ret = fmc4030.FMC4030_Get_Auto_File(id, i, fileName);
//
//            // 只截取\0之前的有效字符（避免残留数据）
//            int len = 0;
//            while (len < fileName.length && fileName[len] != '\0') {
//                len++;
//            }
//            String fileNameStr = new String(fileName, 0, len);
//
//            System.out.println("文件" + i + ":" + fileNameStr + "：返回"+ ret);
//        }

        ret = fmc4030.FMC4030_Close_Device(id);
        System.out.println(ret);
    }
}