## EasyCon（伊机控）

一款用于自动化Switch的上位机控制程序，可以实现全自动孵蛋、放生、极巨战寻找目标、过帧、SL紫光、无限开车、合化石、挖矿、打战斗塔等操作。



![image-20220108012539217](https://s2.loli.net/2022/01/08/fZGywpk3mIh5Yde.png)

![image-20220108012549502](https://s2.loli.net/2022/01/08/SlCY95UFXJiIbuL.png)

* 发布版为不同用户设计了三种不同运行方式（脚本和固件均相互兼容）。

* 【联机模式】通过电脑控制单片机，脚本在电脑端运行，可以看到运行过程，可以使用虚拟手柄直接操作NS。

* 【烧录模式】连线写入程序，脚本在单片机上运行，运行时不需要电脑，但电脑可以控制运行/停止。支持一些极限效率脚本。

* 【固件模式】生成固件后手动刷入，适合没有USB-TTL线的情况，效果和烧录模式类似。

* 脚本使用自己设计的语言，致力于容易学习、结构简单以及可编译成单片机用的字节码。

* 设计方向是简单易学，人人可用，不需要搞C语言就能自己定制脚本并达到最高效率。


使用obs虚拟摄像头： [文档](./docs/obs-virtual-cam.md)
  

# 相关仓库

单片机端指令集：  

> https://docs.qq.com/sheet/DZm1ydlZadkpncUNo?c=A88A0AZ0


单片机仓库：

> https://github.com/EasyConNS/EasyMCU



# 作者

- [铃落](https://github.com/nukieberry)
- [elmagnifico](https://github.com/elmagnificogi)
- [ca1e(当前维护者)](https://github.com/ca1e)



# 感谢

> https://github.com/secile/UsbCamera
