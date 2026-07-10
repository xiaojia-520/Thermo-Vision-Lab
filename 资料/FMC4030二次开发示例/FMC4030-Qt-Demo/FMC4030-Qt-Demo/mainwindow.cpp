#include "mainwindow.h"
#include "ui_mainwindow.h"

#include "FMC4030-Dll.h"

MainWindow::MainWindow(QWidget *parent) :
    QMainWindow(parent),
    ui(new Ui::MainWindow)
{
    ui->setupUi(this);

    this->timer = new QTimer(this);
    this->timer->setTimerType(Qt::PreciseTimer);
    connect(this->timer, SIGNAL(timeout()), this, SLOT(timer_callback()));
    this->timer->start(50);

    this->isConnect = 0;
}

MainWindow::~MainWindow()
{
    if(this->timer)
        delete this->timer;
    this->timer = 0;


    delete ui;
}

void MainWindow::timer_callback(void)
{
    struct machine_status ms;
    if(this->isConnect)
    {
        int res = FMC4030_Get_Machine_Status(this->id, (unsigned char*)&ms);
        if(res == 0)
        {
            ui->lcd_X->display(ms.realPos[0]);
            ui->lcd_Y->display(ms.realPos[1]);
            ui->lcd_Z->display(ms.realPos[2]);
        }
    }
}

void MainWindow::on_pb_Connect_clicked()
{
    if(this->isConnect)
    {
        FMC4030_Close_Device(this->id);
        ui->pb_Connect->setText("连接");
        this->isConnect = 0;
        return;
    }

    this->id = 0;
    int res = FMC4030_Open_Device(this->id, ui->le_Ip->text().toLatin1().data(), ui->le_Port->text().toInt());
    if(res != 0)
    {
        qDebug()<<"控制连接失败，错误代码："<<res;
        return;
    }

    ui->pb_Connect->setText("断开连接");
    this->isConnect = 1;
    return;
}

void MainWindow::on_pb_XF_pressed()
{
    if(this->isConnect)
    {
        FMC4030_Jog_Single_Axis(this->id, 0, 999999, 50, 200, 200, 1);
    }
}

void MainWindow::on_pb_XF_released()
{
    if(this->isConnect)
    {
        FMC4030_Stop_Single_Axis(this->id, 0, 1);
    }
}

void MainWindow::on_pb_XR_pressed()
{
    if(this->isConnect)
    {
        FMC4030_Jog_Single_Axis(this->id, 0, -999999, 50, 200, 200, 1);
    }
}

void MainWindow::on_pb_XR_released()
{
    if(this->isConnect)
    {
        FMC4030_Stop_Single_Axis(this->id, 0, 1);
    }
}

void MainWindow::on_pb_YF_pressed()
{
    if(this->isConnect)
    {
        FMC4030_Jog_Single_Axis(this->id, 1, 999999, 50, 200, 200, 1);
    }
}

void MainWindow::on_pb_YF_released()
{
    if(this->isConnect)
    {
        FMC4030_Stop_Single_Axis(this->id, 1, 1);
    }
}

void MainWindow::on_pb_YR_pressed()
{
    if(this->isConnect)
    {
        FMC4030_Jog_Single_Axis(this->id, 1, -999999, 50, 200, 200, 1);
    }
}

void MainWindow::on_pb_YR_released()
{
    if(this->isConnect)
    {
        FMC4030_Stop_Single_Axis(this->id, 1, 1);
    }
}

void MainWindow::on_pb_ZF_pressed()
{
    if(this->isConnect)
    {
        FMC4030_Jog_Single_Axis(this->id, 2, 999999, 50, 200, 200, 1);
    }
}

void MainWindow::on_pb_ZF_released()
{
    if(this->isConnect)
    {
        FMC4030_Stop_Single_Axis(this->id, 2, 1);
    }
}

void MainWindow::on_pb_ZR_pressed()
{
    if(this->isConnect)
    {
        FMC4030_Jog_Single_Axis(this->id, 2, -999999, 50, 200, 200, 1);
    }
}

void MainWindow::on_pb_ZR_released()
{
    if(this->isConnect)
    {
        FMC4030_Stop_Single_Axis(this->id, 2, 1);
    }
}
