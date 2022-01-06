## EasyCon（伊机控）

![EasyCon](https://user-images.githubusercontent.com/55907281/74600747-be206580-5063-11ea-8b5b-21795e7ab6cf.png)
* 发布版，不需要采集卡，并且为不同用户设计了三种不同运行方式（脚本和固件均相互兼容）。
* 【联机模式】通过电脑控制单片机，脚本在电脑端运行，可以看到运行过程，可以使用虚拟手柄直接操作NS。
* 【烧录模式】连线写入程序，脚本在单片机上运行，运行时不需要电脑，但电脑可以控制运行/停止。支持一些极限效率脚本。
* 【固件模式】生成固件后手动刷入，适合没有USB-TTL线的情况，效果和烧录模式类似。
* 脚本使用自己设计的语言，致力于容易学习、结构简单以及可编译成单片机用的字节码。
* 设计方向是简单易学，人人可用，不需要搞C语言就能自己定制脚本并达到最高效率。
* 与自用版使用相同的固件。  
* 感谢：https://github.com/secile/UsbCamera
* 虚拟摄像头插件：https://github.com/Fenrirthviti/obs-virtual-cam
  
单片机端指令集：  
https://docs.qq.com/sheet/DZm1ydlZadkpncUNo?c=A88A0AZ0
