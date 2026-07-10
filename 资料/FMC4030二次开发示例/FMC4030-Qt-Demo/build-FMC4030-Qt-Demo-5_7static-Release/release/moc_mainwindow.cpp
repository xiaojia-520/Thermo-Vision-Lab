/****************************************************************************
** Meta object code from reading C++ file 'mainwindow.h'
**
** Created by: The Qt Meta Object Compiler version 67 (Qt 5.7.0)
**
** WARNING! All changes made in this file will be lost!
*****************************************************************************/

#include "../../FMC4030-Qt-Demo/mainwindow.h"
#include <QtCore/qbytearray.h>
#include <QtCore/qmetatype.h>
#if !defined(Q_MOC_OUTPUT_REVISION)
#error "The header file 'mainwindow.h' doesn't include <QObject>."
#elif Q_MOC_OUTPUT_REVISION != 67
#error "This file was generated using the moc from 5.7.0. It"
#error "cannot be used with the include files from this version of Qt."
#error "(The moc has changed too much.)"
#endif

QT_BEGIN_MOC_NAMESPACE
struct qt_meta_stringdata_MainWindow_t {
    QByteArrayData data[16];
    char stringdata0[259];
};
#define QT_MOC_LITERAL(idx, ofs, len) \
    Q_STATIC_BYTE_ARRAY_DATA_HEADER_INITIALIZER_WITH_OFFSET(len, \
    qptrdiff(offsetof(qt_meta_stringdata_MainWindow_t, stringdata0) + ofs \
        - idx * sizeof(QByteArrayData)) \
    )
static const qt_meta_stringdata_MainWindow_t qt_meta_stringdata_MainWindow = {
    {
QT_MOC_LITERAL(0, 0, 10), // "MainWindow"
QT_MOC_LITERAL(1, 11, 14), // "timer_callback"
QT_MOC_LITERAL(2, 26, 0), // ""
QT_MOC_LITERAL(3, 27, 21), // "on_pb_Connect_clicked"
QT_MOC_LITERAL(4, 49, 16), // "on_pb_XF_pressed"
QT_MOC_LITERAL(5, 66, 17), // "on_pb_XF_released"
QT_MOC_LITERAL(6, 84, 16), // "on_pb_XR_pressed"
QT_MOC_LITERAL(7, 101, 17), // "on_pb_XR_released"
QT_MOC_LITERAL(8, 119, 16), // "on_pb_YF_pressed"
QT_MOC_LITERAL(9, 136, 17), // "on_pb_YF_released"
QT_MOC_LITERAL(10, 154, 16), // "on_pb_YR_pressed"
QT_MOC_LITERAL(11, 171, 17), // "on_pb_YR_released"
QT_MOC_LITERAL(12, 189, 16), // "on_pb_ZF_pressed"
QT_MOC_LITERAL(13, 206, 17), // "on_pb_ZF_released"
QT_MOC_LITERAL(14, 224, 16), // "on_pb_ZR_pressed"
QT_MOC_LITERAL(15, 241, 17) // "on_pb_ZR_released"

    },
    "MainWindow\0timer_callback\0\0"
    "on_pb_Connect_clicked\0on_pb_XF_pressed\0"
    "on_pb_XF_released\0on_pb_XR_pressed\0"
    "on_pb_XR_released\0on_pb_YF_pressed\0"
    "on_pb_YF_released\0on_pb_YR_pressed\0"
    "on_pb_YR_released\0on_pb_ZF_pressed\0"
    "on_pb_ZF_released\0on_pb_ZR_pressed\0"
    "on_pb_ZR_released"
};
#undef QT_MOC_LITERAL

static const uint qt_meta_data_MainWindow[] = {

 // content:
       7,       // revision
       0,       // classname
       0,    0, // classinfo
      14,   14, // methods
       0,    0, // properties
       0,    0, // enums/sets
       0,    0, // constructors
       0,       // flags
       0,       // signalCount

 // slots: name, argc, parameters, tag, flags
       1,    0,   84,    2, 0x0a /* Public */,
       3,    0,   85,    2, 0x08 /* Private */,
       4,    0,   86,    2, 0x08 /* Private */,
       5,    0,   87,    2, 0x08 /* Private */,
       6,    0,   88,    2, 0x08 /* Private */,
       7,    0,   89,    2, 0x08 /* Private */,
       8,    0,   90,    2, 0x08 /* Private */,
       9,    0,   91,    2, 0x08 /* Private */,
      10,    0,   92,    2, 0x08 /* Private */,
      11,    0,   93,    2, 0x08 /* Private */,
      12,    0,   94,    2, 0x08 /* Private */,
      13,    0,   95,    2, 0x08 /* Private */,
      14,    0,   96,    2, 0x08 /* Private */,
      15,    0,   97,    2, 0x08 /* Private */,

 // slots: parameters
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,
    QMetaType::Void,

       0        // eod
};

void MainWindow::qt_static_metacall(QObject *_o, QMetaObject::Call _c, int _id, void **_a)
{
    if (_c == QMetaObject::InvokeMetaMethod) {
        MainWindow *_t = static_cast<MainWindow *>(_o);
        Q_UNUSED(_t)
        switch (_id) {
        case 0: _t->timer_callback(); break;
        case 1: _t->on_pb_Connect_clicked(); break;
        case 2: _t->on_pb_XF_pressed(); break;
        case 3: _t->on_pb_XF_released(); break;
        case 4: _t->on_pb_XR_pressed(); break;
        case 5: _t->on_pb_XR_released(); break;
        case 6: _t->on_pb_YF_pressed(); break;
        case 7: _t->on_pb_YF_released(); break;
        case 8: _t->on_pb_YR_pressed(); break;
        case 9: _t->on_pb_YR_released(); break;
        case 10: _t->on_pb_ZF_pressed(); break;
        case 11: _t->on_pb_ZF_released(); break;
        case 12: _t->on_pb_ZR_pressed(); break;
        case 13: _t->on_pb_ZR_released(); break;
        default: ;
        }
    }
    Q_UNUSED(_a);
}

const QMetaObject MainWindow::staticMetaObject = {
    { &QMainWindow::staticMetaObject, qt_meta_stringdata_MainWindow.data,
      qt_meta_data_MainWindow,  qt_static_metacall, Q_NULLPTR, Q_NULLPTR}
};


const QMetaObject *MainWindow::metaObject() const
{
    return QObject::d_ptr->metaObject ? QObject::d_ptr->dynamicMetaObject() : &staticMetaObject;
}

void *MainWindow::qt_metacast(const char *_clname)
{
    if (!_clname) return Q_NULLPTR;
    if (!strcmp(_clname, qt_meta_stringdata_MainWindow.stringdata0))
        return static_cast<void*>(const_cast< MainWindow*>(this));
    return QMainWindow::qt_metacast(_clname);
}

int MainWindow::qt_metacall(QMetaObject::Call _c, int _id, void **_a)
{
    _id = QMainWindow::qt_metacall(_c, _id, _a);
    if (_id < 0)
        return _id;
    if (_c == QMetaObject::InvokeMetaMethod) {
        if (_id < 14)
            qt_static_metacall(this, _c, _id, _a);
        _id -= 14;
    } else if (_c == QMetaObject::RegisterMethodArgumentMetaType) {
        if (_id < 14)
            *reinterpret_cast<int*>(_a[0]) = -1;
        _id -= 14;
    }
    return _id;
}
QT_END_MOC_NAMESPACE
