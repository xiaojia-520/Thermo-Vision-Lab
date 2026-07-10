# GCC = arm-fsl-linux-gnueabi-gcc
# AR = arm-fsl-linux-gnueabi-ar

GCC = g++ 
AR = ar

all:
	$(GCC) -o fmc4030 main.c -L. -lFMC4030-Lib

clean:
	rm *.o *.a *.so
