#-------------------------------------------------
#
# Project created by QtCreator 2022-04-19T21:29:13
#
#-------------------------------------------------

QT       += core gui

greaterThan(QT_MAJOR_VERSION, 4): QT += widgets

TARGET = FMC4030-Qt-Demo
TEMPLATE = app

LIBS    += -L$$PWD/ -lFMC4030-Dll

SOURCES += main.cpp\
        mainwindow.cpp

HEADERS  += mainwindow.h \
    FMC4030-Dll.h

FORMS    += mainwindow.ui
