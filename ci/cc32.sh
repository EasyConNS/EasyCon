#!/bin/sh
# Linux CI 的 C99 32 位包装器；ECSVM_CC 指向本文件，CvmRunner 与 budget 共用。
exec gcc -m32 "$@"
