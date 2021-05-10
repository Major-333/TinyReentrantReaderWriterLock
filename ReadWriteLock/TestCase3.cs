using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ReadWriteLock
{
    /* 测试可重入读写锁
     * 写锁可以重入写锁，读锁可以重入写锁，但是写锁不可以重入读锁。因为写操作应该对读操作可见。
     */
    class TestCase3
    {
        public void Test()
        {
            System.Console.WriteLine("\nTest case 3 start!");
            ReentrantReaderWriterLock rwLock = new ReentrantReaderWriterLock();
            ManualResetEvent mre = new ManualResetEvent(false);
            int threadCount = 2;
            // 测试读锁重入写锁
            Task.Run(() =>
            {
                rwLock.EnterWriteLock();
                rwLock.EnterReadLock();
                rwLock.EnterReadLock();
                System.Console.WriteLine("读锁重入写锁成功");
                rwLock.ExitReadLock();
                rwLock.ExitReadLock();
                rwLock.ExitWriteLock();
                Interlocked.Decrement(ref threadCount);
                if (threadCount == 0)
                {
                    mre.Set();
                }
            });
            // 测试写锁重入写锁
            Task.Run(() =>
            {
                rwLock.EnterWriteLock();
                rwLock.EnterWriteLock();
                rwLock.EnterWriteLock();
                System.Console.WriteLine("写锁重入写锁成功");
                rwLock.ExitWriteLock();
                rwLock.ExitWriteLock();
                rwLock.ExitWriteLock();
                Interlocked.Decrement(ref threadCount);
                if (threadCount == 0)
                {
                    mre.Set();
                }
            });

            mre.WaitOne();
            // 测试读锁重入读锁
            Task.Run(() =>
            {
                rwLock.EnterReadLock();
                rwLock.EnterReadLock();
                System.Console.WriteLine("读锁重入读锁成功");
                rwLock.EnterReadLock();
                rwLock.EnterReadLock();
            });
        }
    }

}
