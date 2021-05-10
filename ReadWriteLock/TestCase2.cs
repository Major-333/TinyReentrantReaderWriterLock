using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReadWriteLock
{
    /*  可重入读写锁性能性能测试
     *  和与只用 Monitor保护临界资源的方法作为 Baseline做性能对比
     *  
     *  测试条件与参数：
     *  创建 1024个读写者线程，利用随机数产生器控制写者比例在 10%。并记录随机数，保证两次实验随机数相同。
     *  当 1024的读写者线程全部执行结束后输出读者和写者的平均等待时间
     *  
     *  测试结果：
     *  写者平均等待时间：大约是 Baseline方法（只使用一个Monitor）的 5倍
     *  读者平均等待时间：大约是 Baseline方法（只使用一个Monitor）的 1.6倍
     *  
     *  注意：由于为了保证写优先（解决 Second readers–writers problem）读写锁的读者并发性会受一定程度限制
     */
    class TestCase2
    {
        long readWaitTime;
        long writeWaitTime;

        int writerThreadNum;
        int readerThreadNum;
        int totalThreadNum;

        private ReentrantReaderWriterLock readerWriterLock;

        int finishedWorkerCount;
        private AutoResetEvent finished;

        private object monitorLockObj;
        private int baselineWriterWaitCount;

        public TestCase2()
        {
            readWaitTime = 0;
            writeWaitTime = 0;
            readerWriterLock = new ReentrantReaderWriterLock();

            writerThreadNum = 0;
            readerThreadNum = 0;
            totalThreadNum = 1024;
            finishedWorkerCount = 0;
            finished = new AutoResetEvent(false);

            monitorLockObj = new object();
            baselineWriterWaitCount = 0;
        }

        private static void Reader(Object obj)
        {
            TestCase2 testCase = (TestCase2)obj;
            Stopwatch stopwatch = new Stopwatch();
            // 记录获取读锁的等待时间
            stopwatch.Start();
            testCase.readerWriterLock.EnterReadLock();
            stopwatch.Stop();
            // 原子操作，更新读者等待总时间
            Interlocked.Add(ref testCase.readWaitTime, stopwatch.ElapsedMilliseconds);
            stopwatch.Reset();
            // 模仿读操作用时
            Thread.Sleep(10);
            // 释放读锁
            testCase.readerWriterLock.ExitReadLock();
            // 原子操作，已完成线程计数器+1，如果全部完成唤醒(通知)主线程
            Interlocked.Add(ref testCase.finishedWorkerCount, 1);
            if (testCase.finishedWorkerCount == testCase.totalThreadNum)
            {
                testCase.finished.Set();
            }
        }

        private static void Writer(Object obj)
        {
            TestCase2 testCase = (TestCase2)obj;
            Stopwatch stopwatch = new Stopwatch();
            // 记录获取写锁的等待时间
            stopwatch.Start();
            Interlocked.Add(ref testCase.baselineWriterWaitCount, 1);
            testCase.readerWriterLock.EnterWriteLock();
            Interlocked.Add(ref testCase.baselineWriterWaitCount, -1);
            stopwatch.Stop();
            // 原子操作，更新写者等待总时间
            Interlocked.Add(ref testCase.writeWaitTime, stopwatch.ElapsedMilliseconds);
            stopwatch.Reset();
            // 模仿写操作用时
            Thread.Sleep(100);
            // 释放写锁
            testCase.readerWriterLock.ExitWriteLock();
            // 原子操作，已完成线程计数器+1，如果全部完成唤醒(通知)主线程
            Interlocked.Add(ref testCase.finishedWorkerCount, 1);
            if (testCase.finishedWorkerCount == testCase.totalThreadNum)
            {
                testCase.finished.Set();
            }
        }

        private static void ReaderBaseline(Object obj)
        {
            TestCase2 testCase = (TestCase2)obj;
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            while (testCase.baselineWriterWaitCount != 0) ;
            Monitor.Enter(testCase.monitorLockObj);
            stopwatch.Stop();
            // 原子操作，更新写者等待总时间
            Interlocked.Add(ref testCase.readWaitTime, stopwatch.ElapsedMilliseconds);
            // 模仿读操作用时
            Thread.Sleep(10);
            Monitor.Exit(testCase.monitorLockObj);
            // 原子操作，已完成线程计数器+1，如果全部完成唤醒(通知)主线程
            Interlocked.Add(ref testCase.finishedWorkerCount, 1);
            if (testCase.finishedWorkerCount == testCase.totalThreadNum)
            {
                testCase.finished.Set();
            }
        }

        private static void WriterBaseline(Object obj)
        {
            TestCase2 testCase = (TestCase2)obj;
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            Monitor.Enter(testCase.monitorLockObj);
            stopwatch.Stop();
            // 原子操作，更新写者等待总时间
            Interlocked.Add(ref testCase.writeWaitTime, stopwatch.ElapsedMilliseconds);
            // 模仿写操作用时
            Thread.Sleep(100);
            Monitor.Exit(testCase.monitorLockObj);
            // 原子操作，已完成线程计数器+1，如果全部完成唤醒(通知)主线程
            Interlocked.Add(ref testCase.finishedWorkerCount, 1);
            if (testCase.finishedWorkerCount == testCase.totalThreadNum)
            {
                testCase.finished.Set();
            }
        }

        private void printTestResult(Stopwatch stopwatch, String lockName)
        {
            Console.WriteLine(lockName + "所耗总时间{0}ms", stopwatch.ElapsedMilliseconds);
            Console.WriteLine(lockName + "读者等待时间：{0}ms，"+ lockName + "写者等待时间{1}ms", readWaitTime, writeWaitTime);
            Console.WriteLine(lockName + "读者平均等待时间：{0}ms，" + lockName + "写者平均等待时间{1}ms", readWaitTime / readerThreadNum, writeWaitTime / writerThreadNum);
        }

        public void Test()
        {
            System.Console.WriteLine("\nTest Case 2 start!");

            // 使用stopwatch来测算耗时
            Stopwatch stopwatch = new Stopwatch();
            var rand = new Random();
            int[] randNumList = new int[totalThreadNum];
            // 使用随机数，模拟10%的线程为写者线程，同时记录随机数以保证两次测试的公平性
            for (int i = 0; i < totalThreadNum; i++)
                randNumList[i] = rand.Next(20);

            // 使用我们自己实现的 ReentrantReaderWriterLock 进行测试
            stopwatch.Start();
            for (int i = 0; i < totalThreadNum; i++)
            {
                int rd = randNumList[i];
                // rd范围是0-19,5%的线程为写者
                if(rd == 0)
                {
                    writerThreadNum++;
                    new Thread(Writer).Start(this);
                }
                else
                {
                    readerThreadNum++;
                    new Thread(Reader).Start(this);
                }
            }
            finished.WaitOne();
            stopwatch.Stop();
            printTestResult(stopwatch, "Ours");

            // 归零统计量
            readerThreadNum = 0;
            writerThreadNum = 0;
            readWaitTime = 0;
            writeWaitTime = 0;
            finishedWorkerCount = 0;
            stopwatch = new Stopwatch();
            finished.Reset();

            // 测试使用Monitor来完成读写任务，作为测试的Baseline
            stopwatch.Start();
            for (int i = 0; i < totalThreadNum; i++)
            {
                int rd = randNumList[i];
                // rd范围是0-9,10%的线程为写者
                if (rd == 0)
                {
                    writerThreadNum++;
                    new Thread(WriterBaseline).Start(this);
                }
                else
                {
                    readerThreadNum++;
                    new Thread(ReaderBaseline).Start(this);
                }
            }
            finished.WaitOne();
            stopwatch.Stop();
            printTestResult(stopwatch, "Baseline");
        }
    }
}
