using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ReadWriteLock
{
    /*1
     * 
     */
    class TestCase3
    {
        public void Test()
        {
            System.Console.WriteLine("\nTest case 3 start!");
            ReentrantReaderWriterLock rwLock = new ReentrantReaderWriterLock();
            ManualResetEvent mre = new ManualResetEvent(false);
            int threadCount = 2;

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
