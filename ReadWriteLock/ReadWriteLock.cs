using System;
using System.Threading;
namespace ReadWriteLock
{
    /*  可重入读写锁
     *  这个读写锁设计是为了符合第二读写者问题。
     *  可参考：https://en.wikipedia.org/wiki/Readers%E2%80%93writers_problem#Second_readers%E2%80%93writers_problem
     *  支持最多 65535 个重入写锁，支持最多 65535 个重入读锁
     *  满足以下约束条件
     *  - 读者可以并发的访问共享数据（读者并发读取共享资源）
     *  - 当有写者进行写入共享数据时，读者和其他写者应当等待（写者独占资源资源）
     *  - 当有写者线程请求写入共享数据后，不应该允许新的读者访问共享数据（写优先，写者不会发生饥饿现象）
     *  - 支持锁的可重入（防止死锁）。写锁可以重入写锁，读锁可以重入写锁，但是写锁不可以重入读锁。因为写操作应该对读操作可见。
     */
    class ReentrantReaderWriterLock
    {
        private int readCount;                                          // 进入临界区(Critical section)的读者数量
        private int writeCount;                                         // 进入临界区(Critical section)的写者数量

        private static object readCountLock = new object();             // 被 Monitor用来保护读者计数器
        private static object writeCountLock = new object();            // 被 Monitor用来保护写者计数器

        private AutoResetEvent writeEvent;                              // 用来保护写操作
        private AutoResetEvent readEvent;                               // 用来保护读操作

        private static object _lock = new object();                     // 被 Monitor用来保护读者的启动逻辑不被其他读者影响,
                                                                        // 防止太多读者等待 readEvent，因此写者有更高的机会被 readEvent信号唤醒。

        private int state;                                              // 为了支持可重入维护的状态量。高 16位表示重入的读者数量，低16位表示重入的写者数量
        private int exclusiveThreadId;                                  // 独占当前读写锁的线程 ID，在读共享模式下为 -1                  
        private static object stateLock = new object();                 // 被 Monitor用来保护 state变量


        private const int SHARED_SHIFT = 16;                            // 标识读者重入数在高 16位，自加前应先左移
        private const int SHARED_UNIT = (1 << SHARED_SHIFT);            // 读者重入时 state变量增加的单位
        private const int MAX_COUNT = (1 << SHARED_SHIFT) - 1;          // 支持的最大的重入读/写者数量
        private const int EXCLUSIVE_MASK = (1 << SHARED_SHIFT) - 1;     // 用来获取重入写者数量的掩码

        static int sharedCount(int c) { return c >> SHARED_SHIFT; }     // 直接获取当前已重入的读者数量的函数
        static int exclusiveCount(int c) { return c & EXCLUSIVE_MASK; } // 直接获取当前已重入的写者数量的函数

        /* 构造函数
         */
        public ReentrantReaderWriterLock()
        {
            writeEvent = new AutoResetEvent(true);
            readEvent = new AutoResetEvent(true);
            _lock = -1;
            readCount = 0;
            writeCount = 0;
            exclusiveThreadId = -1;
        }

        /*  读者请求读写锁
         * 
         *  先获取当前线程 ID与独占读写锁 ID比较，当ID相同时进行重入操作，否则进行竞争。
         * 
         *  读者重入：
         *  对 state变量的高 16位加 1，然后函数结束，读进程不受阻塞。
         * 
         *  读者竞争读写锁：
         *  请求保护 readEvent的 _lock互斥量，请求 readEvent一个信号量获取读权限
         *  * 注意 *：每次最多只要一个读者请求读资源，因此写者有更高的机会抢占读资源
         *  有读权限后获取 readCountLock访问临界资源 readCount并加一
         *  第一个读者将占有写资源 writeEvent
         *  回溯释放占有的互斥量（锁），此时读者可以并发读取临界区资源
         */
        public void EnterReadLock()
        {
            int id = Environment.CurrentManagedThreadId;
            Monitor.Enter(stateLock);
            if (id == exclusiveThreadId)
            {
                //Reentrant
                state += SHARED_UNIT;
                Monitor.Exit(stateLock);
                return;
            }
            Monitor.Exit(stateLock);


            Monitor.Enter(_lock);
            readEvent.WaitOne();
            Monitor.Enter(readCountLock);
            readCount++;
            if (readCount == 1)
            {
                writeEvent.WaitOne();
            }
            Monitor.Exit(readCountLock);
            readEvent.Set();
            Monitor.Exit(_lock);
        }

        /*  读者释放读写锁
         * 
         *  先获取当前线程 ID与独占读写锁 ID比较，当ID相同时进行重入释放操作，否则进行正常释放。
         * 
         *  读者重入释放读写锁：
         *  对 state变量的高 16位减 1，然后函数结束，读进程退出不受阻塞。
         * 
         *  读者正常释放读写锁：
         *  请求获取 readCountLock访问临界资源 readCount并减一
         *  最后一个读者将释放之前占有的写资源 writeEvent
         *  回溯释放占有的互斥量（锁），此时读线程可以退出
         */
        public void ExitReadLock()
        {
            int id = Environment.CurrentManagedThreadId;
            Monitor.Enter(stateLock);
            if (id == exclusiveThreadId)
            {
                //Reentrant
                state -= SHARED_UNIT;
                Monitor.Exit(stateLock);
                return;
            }
            Monitor.Exit(stateLock);

            Monitor.Enter(readCountLock);
            readCount--;
            if (readCount == 0)
            {
                writeEvent.Set();
            }
            Monitor.Exit(readCountLock);
        }


        /*  写者请求读写锁
         * 
         *  先获取当前线程 ID与独占读写锁 ID比较，当ID相同并且没有读重入时进行重入操作，否则进行竞争。
         * 
         *  写者重入：
         *  对 state变量的低 16位加 1，然后函数结束，写进程不受阻塞。
         * 
         *  写者竞争读写锁：
         *  获取 writeCountLock访问临界资源 writeCount并加一
         *  第一个写者将请求占有读资源 readEvent
         *  释放 writeCountLock，请求占有写资源 writeEvent
         *  此时读者可以独占临界区资源进行写入
         */
        public void EnterWriteLock()
        {
            int id = Environment.CurrentManagedThreadId;
            Monitor.Enter(stateLock);
            int r = sharedCount(state);
            int w = exclusiveCount(state);
            if(w != 0 && id == exclusiveThreadId)
            {
                if(r != 0)
                {
                    throw new Exception("写锁不可重入读锁");
                }
                // Reentrant
                state += 1;
                Monitor.Exit(stateLock);
                
                return;
            }
            Monitor.Exit(stateLock);

            Monitor.Enter(writeCountLock);
            writeCount++;
            if (writeCount == 1)
            {
                readEvent.WaitOne();
            }
            Monitor.Exit(writeCountLock);
            writeEvent.WaitOne();

            Monitor.Enter(stateLock);
            exclusiveThreadId = Environment.CurrentManagedThreadId;
            state += 1;
            Monitor.Exit(stateLock);
        }

        /*  写者释放读写锁
         *  
         *  对 state变量的低 16位减 1
         *  state不为0进行重入释放操作，否则进行正常释放。
         * 
         *  写者重入释放：
         *  函数直接结束，写进程释放不受阻塞。
         * 
         *  写者正常释放：
         *  释放已经独占的写资源  writeEvent
         *  获取 writeCountLock访问临界资源 writeCount并减一
         *  最后一个写者将释放已经占有的读资源 readEvent
         *  释放 writeCountLock
         *  函数退出，写进程释放读写锁结束。
         */
        public void ExitWriteLock()
        {
            int id = Environment.CurrentManagedThreadId;
            Monitor.Enter(stateLock);
            state -= 1;
            if (state != 0)
            {
                Monitor.Exit(stateLock);
                return;
            }
            exclusiveThreadId = -1;
            Monitor.Exit(stateLock);

            writeEvent.Set();
            Monitor.Enter(writeCountLock);
            writeCount--;
            if (writeCount == 0)
            {
                readEvent.Set();
            }
            Monitor.Exit(writeCountLock);
        }

    }
}