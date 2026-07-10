/********************************************************************************
** Form generated from reading UI file 'mainwindow.ui'
**
** Created by: Qt User Interface Compiler version 5.7.0
**
** WARNING! All changes made in this file will be lost when recompiling UI file!
********************************************************************************/

#ifndef UI_MAINWINDOW_H
#define UI_MAINWINDOW_H

#include <QtCore/QVariant>
#include <QtWidgets/QAction>
#include <QtWidgets/QApplication>
#include <QtWidgets/QButtonGroup>
#include <QtWidgets/QHBoxLayout>
#include <QtWidgets/QHeaderView>
#include <QtWidgets/QLCDNumber>
#include <QtWidgets/QLabel>
#include <QtWidgets/QLineEdit>
#include <QtWidgets/QMainWindow>
#include <QtWidgets/QMenuBar>
#include <QtWidgets/QPushButton>
#include <QtWidgets/QStatusBar>
#include <QtWidgets/QToolBar>
#include <QtWidgets/QVBoxLayout>
#include <QtWidgets/QWidget>

QT_BEGIN_NAMESPACE

class Ui_MainWindow
{
public:
    QWidget *centralWidget;
    QVBoxLayout *verticalLayout_2;
    QHBoxLayout *horizontalLayout;
    QLabel *label;
    QLineEdit *le_Ip;
    QLineEdit *le_Port;
    QPushButton *pb_Connect;
    QVBoxLayout *verticalLayout;
    QHBoxLayout *horizontalLayout_2;
    QLCDNumber *lcd_X;
    QPushButton *pb_XF;
    QPushButton *pb_XR;
    QHBoxLayout *horizontalLayout_3;
    QLCDNumber *lcd_Y;
    QPushButton *pb_YF;
    QPushButton *pb_YR;
    QHBoxLayout *horizontalLayout_4;
    QLCDNumber *lcd_Z;
    QPushButton *pb_ZF;
    QPushButton *pb_ZR;
    QMenuBar *menuBar;
    QToolBar *mainToolBar;
    QStatusBar *statusBar;

    void setupUi(QMainWindow *MainWindow)
    {
        if (MainWindow->objectName().isEmpty())
            MainWindow->setObjectName(QStringLiteral("MainWindow"));
        MainWindow->resize(500, 353);
        centralWidget = new QWidget(MainWindow);
        centralWidget->setObjectName(QStringLiteral("centralWidget"));
        verticalLayout_2 = new QVBoxLayout(centralWidget);
        verticalLayout_2->setSpacing(6);
        verticalLayout_2->setContentsMargins(11, 11, 11, 11);
        verticalLayout_2->setObjectName(QStringLiteral("verticalLayout_2"));
        horizontalLayout = new QHBoxLayout();
        horizontalLayout->setSpacing(6);
        horizontalLayout->setObjectName(QStringLiteral("horizontalLayout"));
        label = new QLabel(centralWidget);
        label->setObjectName(QStringLiteral("label"));

        horizontalLayout->addWidget(label);

        le_Ip = new QLineEdit(centralWidget);
        le_Ip->setObjectName(QStringLiteral("le_Ip"));

        horizontalLayout->addWidget(le_Ip);

        le_Port = new QLineEdit(centralWidget);
        le_Port->setObjectName(QStringLiteral("le_Port"));

        horizontalLayout->addWidget(le_Port);

        pb_Connect = new QPushButton(centralWidget);
        pb_Connect->setObjectName(QStringLiteral("pb_Connect"));

        horizontalLayout->addWidget(pb_Connect);


        verticalLayout_2->addLayout(horizontalLayout);

        verticalLayout = new QVBoxLayout();
        verticalLayout->setSpacing(6);
        verticalLayout->setObjectName(QStringLiteral("verticalLayout"));
        horizontalLayout_2 = new QHBoxLayout();
        horizontalLayout_2->setSpacing(6);
        horizontalLayout_2->setObjectName(QStringLiteral("horizontalLayout_2"));
        lcd_X = new QLCDNumber(centralWidget);
        lcd_X->setObjectName(QStringLiteral("lcd_X"));
        lcd_X->setStyleSheet(QStringLiteral("color: rgb(255, 0, 4);"));
        lcd_X->setMode(QLCDNumber::Dec);

        horizontalLayout_2->addWidget(lcd_X);

        pb_XF = new QPushButton(centralWidget);
        pb_XF->setObjectName(QStringLiteral("pb_XF"));

        horizontalLayout_2->addWidget(pb_XF);

        pb_XR = new QPushButton(centralWidget);
        pb_XR->setObjectName(QStringLiteral("pb_XR"));

        horizontalLayout_2->addWidget(pb_XR);

        horizontalLayout_2->setStretch(0, 2);

        verticalLayout->addLayout(horizontalLayout_2);

        horizontalLayout_3 = new QHBoxLayout();
        horizontalLayout_3->setSpacing(6);
        horizontalLayout_3->setObjectName(QStringLiteral("horizontalLayout_3"));
        lcd_Y = new QLCDNumber(centralWidget);
        lcd_Y->setObjectName(QStringLiteral("lcd_Y"));
        lcd_Y->setStyleSheet(QStringLiteral("color: rgb(255, 0, 4);"));

        horizontalLayout_3->addWidget(lcd_Y);

        pb_YF = new QPushButton(centralWidget);
        pb_YF->setObjectName(QStringLiteral("pb_YF"));

        horizontalLayout_3->addWidget(pb_YF);

        pb_YR = new QPushButton(centralWidget);
        pb_YR->setObjectName(QStringLiteral("pb_YR"));

        horizontalLayout_3->addWidget(pb_YR);

        horizontalLayout_3->setStretch(0, 2);

        verticalLayout->addLayout(horizontalLayout_3);

        horizontalLayout_4 = new QHBoxLayout();
        horizontalLayout_4->setSpacing(6);
        horizontalLayout_4->setObjectName(QStringLiteral("horizontalLayout_4"));
        lcd_Z = new QLCDNumber(centralWidget);
        lcd_Z->setObjectName(QStringLiteral("lcd_Z"));
        lcd_Z->setStyleSheet(QStringLiteral("color: rgb(255, 0, 4);"));

        horizontalLayout_4->addWidget(lcd_Z);

        pb_ZF = new QPushButton(centralWidget);
        pb_ZF->setObjectName(QStringLiteral("pb_ZF"));

        horizontalLayout_4->addWidget(pb_ZF);

        pb_ZR = new QPushButton(centralWidget);
        pb_ZR->setObjectName(QStringLiteral("pb_ZR"));

        horizontalLayout_4->addWidget(pb_ZR);

        horizontalLayout_4->setStretch(0, 2);

        verticalLayout->addLayout(horizontalLayout_4);


        verticalLayout_2->addLayout(verticalLayout);

        MainWindow->setCentralWidget(centralWidget);
        menuBar = new QMenuBar(MainWindow);
        menuBar->setObjectName(QStringLiteral("menuBar"));
        menuBar->setGeometry(QRect(0, 0, 500, 23));
        MainWindow->setMenuBar(menuBar);
        mainToolBar = new QToolBar(MainWindow);
        mainToolBar->setObjectName(QStringLiteral("mainToolBar"));
        MainWindow->addToolBar(Qt::TopToolBarArea, mainToolBar);
        statusBar = new QStatusBar(MainWindow);
        statusBar->setObjectName(QStringLiteral("statusBar"));
        MainWindow->setStatusBar(statusBar);

        retranslateUi(MainWindow);

        QMetaObject::connectSlotsByName(MainWindow);
    } // setupUi

    void retranslateUi(QMainWindow *MainWindow)
    {
        MainWindow->setWindowTitle(QApplication::translate("MainWindow", "MainWindow", 0));
        label->setText(QApplication::translate("MainWindow", "IP\345\217\212\347\253\257\345\217\243\357\274\232", 0));
        le_Ip->setText(QApplication::translate("MainWindow", "192.168.0.30", 0));
        le_Port->setText(QApplication::translate("MainWindow", "8088", 0));
        pb_Connect->setText(QApplication::translate("MainWindow", "\350\277\236\346\216\245", 0));
        pb_XF->setText(QApplication::translate("MainWindow", "X+", 0));
        pb_XR->setText(QApplication::translate("MainWindow", "X-", 0));
        pb_YF->setText(QApplication::translate("MainWindow", "Y+", 0));
        pb_YR->setText(QApplication::translate("MainWindow", "Y-", 0));
        pb_ZF->setText(QApplication::translate("MainWindow", "Z+", 0));
        pb_ZR->setText(QApplication::translate("MainWindow", "Z-", 0));
    } // retranslateUi

};

namespace Ui {
    class MainWindow: public Ui_MainWindow {};
} // namespace Ui

QT_END_NAMESPACE

#endif // UI_MAINWINDOW_H
