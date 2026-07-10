#ifndef MAINWINDOW_H
#define MAINWINDOW_H

#include <QMainWindow>
#include <QTimer>
#include <QDebug>

namespace Ui {
class MainWindow;
}

class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    explicit MainWindow(QWidget *parent = 0);
    ~MainWindow();

public slots:
    void timer_callback(void);

private slots:
    void on_pb_Connect_clicked();

    void on_pb_XF_pressed();

    void on_pb_XF_released();

    void on_pb_XR_pressed();

    void on_pb_XR_released();

    void on_pb_YF_pressed();

    void on_pb_YF_released();

    void on_pb_YR_pressed();

    void on_pb_YR_released();

    void on_pb_ZF_pressed();

    void on_pb_ZF_released();

    void on_pb_ZR_pressed();

    void on_pb_ZR_released();

private:
    Ui::MainWindow *ui;

private:
    QTimer* timer;
    int isConnect;
    int id;
};

#endif // MAINWINDOW_H
