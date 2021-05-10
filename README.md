# 读写锁

[TOC]

## 一、项目简介

### 1.1 项目内容

实现一个写优先的读写锁，并且支持可重入功能。

>写优先：即 Second readers–writers problem 写线程拥有更高的优先级
>
>可重入：可重入锁，也叫做递归锁，指的是同一线程外层函数获得锁之后，内层递归函数仍然有获取该锁的代码，但不受影响。即同一个线程可以重复加锁，可以对同一个锁加多次，每次释放的时候回释放一次，直到该线程加锁次数为0，这个线程才释放锁。比如同一个线程中可以对同一个锁加多次写入锁。写线程获取写入锁后可以再次获取读取锁，但是读线程获取读取锁后却不能获取写入锁。

### 1.2 约束条件

- 读者可以并发的访问共享数据（读者并发读取共享资源）
- 当有写者进行写入共享数据时，读者和其他写者应当等待（写者独占资源资源）
- 当有写者线程请求写入共享数据后，不应该允许新的读者访问共享数据（写优先，写者不会发生饥饿现象）
- 支持锁的可重入（防止死锁）。写锁可以重入写锁，读锁可以重入写锁，但是写锁不可以重入读锁。因为写操作应该对读操作可见。

## 二、可重入读写锁原理

###2.1 数据结构

```c#
    class ReentrantReaderWriterLock
    {
        private AutoResetEvent writeEvent;
        private AutoResetEvent readEvent;

        private int readCount;
        private int writeCount;

        private static object _lock = new object();
        private static object readCountLock = new object();
        private static object writeCountLock = new object();

        private int exclusiveThreadId;
        private int state;

        private static object stateLock = new object();

        private const int SHARED_SHIFT = 16;
        private const int SHARED_UNIT = (1 << SHARED_SHIFT);
        private const int MAX_COUNT = (1 << SHARED_SHIFT) - 1;
        private const int EXCLUSIVE_MASK = (1 << SHARED_SHIFT) - 1;

        static int sharedCount(int c) { return c >> SHARED_SHIFT; }
        static int exclusiveCount(int c) { return c & EXCLUSIVE_MASK; }
      
      	public ReentrantReaderWriterLock();
      	public void EnterReadLock();
      	public void ExitReadLock();
      	public void EnterWriteLock();
      	public void ExitWriteLock();
    }
```

- 计数器： readCount 和 writeCount 代表等待进入临界区(Critical section)的读者和写者数量。
- readCountLock 被Monitor用来保护读者计数器，writeCountLock 被Monitor用来保护写者计数器
- writeEvent 用来保护写操作，readEvent 用来保护读操作
- _lock 被Monitor用来保护读者的启动逻辑不被其他读者影响。写优先的实现关键在此（防止写者饥饿），\_lock 防止太多读者等待readEvent，因此写者有更高的机会被readEvent信号唤醒。
- state：为了支持可重入维护的状态量。
  - 高16位表示重入的读者数量，低16位表示重入的写者数量
- exclusiveThreadId：独占当前读写锁的线程ID，在读共享模式下为-1

### 2.2 实现细节

#### 2.2.1 读者请求读写锁

- 先获取当前线程 ID与独占读写锁 ID比较，当ID相同时进行重入操作，否则进行竞争。
- 读者重入：
  - 对 state变量的高 16位加 1，然后函数结束，读进程不受阻塞。
- 读者竞争读写锁：
  - 请求保护 readEvent的 _lock互斥量，请求 readEvent一个信号量获取读权限。（注意：每次最多只要一个读者请求读资源，因此写者有更高的机会抢占读资源）
  - 有读权限后获取 readCountLock访问临界资源 readCount并加一
  - 第一个读者将占有写资源 writeEvent
  - 回溯释放占有的互斥量（锁），此时读者可以并发读取临界区资源

```c#
public void EnterWriteLock()
{
  	// check for Reentrant
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
```

#### 2.2.2 读者释放读写锁

- 先获取当前线程 ID与独占读写锁 ID比较，当ID相同时进行重入释放操作，否则进行正常释放。
- 读者重入释放读写锁：
  - 对 state变量的高 16位减 1，然后函数结束，读进程退出不受阻塞。

- 读者正常释放读写锁：
  - 请求获取 readCountLock访问临界资源 readCount并减一
  - 最后一个读者将释放之前占有的写资源 writeEvent
  - 回溯释放占有的互斥量（锁），此时读线程可以退出

```c#
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
```

#### 2.2.3 写者请求读写锁

- 先获取当前线程 ID与独占读写锁 ID比较，当ID相同并且没有读重入时进行重入操作，否则进行竞争。
- 写者重入：
  -  对 state变量的低 16位加 1，然后函数结束，写进程不受阻塞。
-  写者竞争读写锁：
  - 获取 writeCountLock访问临界资源 writeCount并加一
  - 第一个写者将请求占有读资源 readEvent
  - 释放 writeCountLock，请求占有写资源 writeEvent
  - 此时读者可以独占临界区资源进行写入

```c#
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
```

#### 2.2.4 写者释放读写锁

- 对 state变量的低 16位减 1
- state不为0进行重入释放操作，否则进行正常释放。
- 写者重入释放：
  - 函数直接结束，写进程释放不受阻塞。
- 写者正常释放：
  - 释放已经独占的写资源  writeEvent
  - 获取 writeCountLock访问临界资源 writeCount并减一
  - 最后一个写者将释放已经占有的读资源 readEvent
  - 释放 writeCountLock
  - 函数退出，写进程释放读写锁结束。

```c#
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
```



## 三、正确性测试

### 3.1 读写锁正确性测试

> 测试代码与官方文档一致
>
> https://docs.microsoft.com/en-us/dotnet/api/system.threading.readerwriterlockslim

#### 3.1.1 测试读写锁是否符合约束。

- 实现了 SynchronizedCache 一个多线程共享的缓冲区（数据结构为：Key-Value）
- 使用 我们自己实现的 ReentrantReaderWriterLock 来保证读者写者正确同步性。使用 SynchronizedCache 来保证与官方文档测试读写锁接口正确。

#### 3.1.2 测试过程：

-  启动写者线程写入写入

```c#
tasks.Add(Task.Run(() =>
                   {
                     String[] vegetables = { "broccoli", "cauliflower",
                                            "carrot", "sorrel", "baby turnip",
                                            "beet", "brussel sprout",
                                            "cabbage", "plantain",
                                            "spinach", "grape leaves",
                                            "lime leaves", "corn",
                                            "radish", "cucumber",
                                            "raddichio", "lima beans" };
                     for (int ctr = 1; ctr <= vegetables.Length; ctr++)
                       sc.Add(ctr, vegetables[ctr - 1]);

                     itemsWritten = vegetables.Length;
                     Console.WriteLine("Task {0} wrote {1} items\n",
                                       Task.CurrentId, itemsWritten);
                   }));
```

- 启动两个读者线程，一个从字典的首部读到尾部并输出，一个从字典的尾部读到首部输出

```c#
for (int ctr = 0; ctr <= 1; ctr++)
{
    bool desc = Convert.ToBoolean(ctr);
    tasks.Add(Task.Run(() =>
    {
        int start, last, step;
        int items;
        do
        {
            String output = String.Empty;
            items = sc.Count;
            if (!desc)
            {
                start = 1;
                step = 1;
                last = items;
            }
            else
            {
                start = items;
                step = -1;
                last = 1;
            }
            for (int index = start; desc ? index >= last : index <= last; index += step)
                output += String.Format("[{0}] ", sc.Read(index));
            Console.WriteLine("Task {0} read {1} items: {2}\n",
            Task.CurrentId, items, output);
    		} while (items < itemsWritten | itemsWritten == 0);
    }));
}
```

####3.1.3 测试结果：

![output-001](/Users/major333/work/dotnet/ReadWriteLock/TinyReentrantReaderWriterLock/imgs/output-001.png)



### 3.2 可重入正确性测试

> 写锁可以重入写锁，读锁可以重入写锁，但是写锁不可以重入读锁。因为写操作应该对读操作可见。

#### 3.2.1 测试读锁重入写锁

```c#
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
```

#### 3.2.2 测试写锁重入写锁

```c#
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
```

#### 3.2.3 测试读锁重入读锁

```c#
// 测试读锁重入读锁
Task.Run(() =>
         {
           rwLock.EnterReadLock();
           rwLock.EnterReadLock();
           System.Console.WriteLine("读锁重入读锁成功");
           rwLock.EnterReadLock();
           rwLock.EnterReadLock();
         });
```



## 四、 性能分析

- 可重入读写锁性能性能测试，和与只用 Monitor保护临界资源的方法作为 Baseline做性能对比

>测试代码见：TestCase2.cs 文件

### 4.1 测试条件与参数：

- 读写者线程总数:1024个
- 假设读取时间:10ms，写入时间:100ms
- 写者比例:5%，利用随机数产生器控制读写者比例。并记录随机数，保证两次实验随机数相同。
- 当 1024的读写者线程全部执行结束后输出读者和写者的平均等待时间

### 4.2 测试结果：

- 写者平均等待时间：大约是 Baseline方法（只使用一个Monitor）的 71倍
- 读者平均等待时间：大约是 Baseline方法（只使用一个Monitor）的 30倍
- 写者比例越低时，与Baseline 相比优化效果越好。（由于更好的利用了并发读取）

> 注意：由于为了保证写优先（解决 Second readers–writers problem）读写锁的读者并发性会受一定程度限制



## 五、优缺点与进一步优化

- 优点：
  - 解决 Second readers–writers problem，满足了所需的约束条件
  - 支持了锁的可重入
- 进一步优化：
  - 目前暂不支持锁的升级