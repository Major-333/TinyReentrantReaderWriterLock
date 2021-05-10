using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ReadWriteLock
{
    /*  SynchronizedCache 是一个多线程共享的缓冲区（数据结构为：Key-Value）
     *  使用 我们自己实现的 ReentrantReaderWriterLock 来保证读者写者正确同步性 
     *  使用 SynchronizedCache 来保证与官方文档测试读写锁接口正确
     *  参考自MSDN https://docs.microsoft.com/en-us/dotnet/api/system.threading.readerwriterlockslim
     */
    public class SynchronizedCache
    {
        private ReentrantReaderWriterLock cacheLock = new ReentrantReaderWriterLock();
        private Dictionary<int, string> innerCache = new Dictionary<int, string>();

        public int Count
        { get { return innerCache.Count; } }

        public string Read(int key)
        {
            cacheLock.EnterReadLock();
            try
            {
                return innerCache[key];
            }
            finally
            {
                cacheLock.ExitReadLock();
            }
        }

        public void Add(int key, string value)
        {
            cacheLock.EnterWriteLock();
            try
            {
                innerCache.Add(key, value);
            }
            finally
            {
                cacheLock.ExitWriteLock();
            }
        }

        public void Delete(int key)
        {
            cacheLock.EnterWriteLock();
            try
            {
                innerCache.Remove(key);
            }
            finally
            {
                cacheLock.ExitWriteLock();
            }
        }

    }
}