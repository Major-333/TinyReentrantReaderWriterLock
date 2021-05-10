using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ReadWriteLock
{
    /* 读写锁正确性测试样例
     * 测试样例与官方文档测试一致，参考自MSDN https://docs.microsoft.com/en-us/dotnet/api/system.threading.readerwriterlockslim
     * 期望输出同样来自MSDN测试结果
     */
    public class TestCase1
    {
        public void Test()
        {
            System.Console.WriteLine("\nTest case 1 start!");
            var sc = new SynchronizedCache();
            var tasks = new List<Task>();
            int itemsWritten = 0;
            // 启动写者线程
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
            //启动两个读者线程，一个从字典的首部读到尾部并输出，一个从字典的尾部读到首部输出
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
            // 等待所有线程完成.
            Task.WaitAll(tasks.ToArray());
            // 输出缓存中最终的内容.
            Console.WriteLine();
            Console.WriteLine("Values in synchronized cache: ");
            for (int ctr = 1; ctr <= sc.Count; ctr++)
                Console.WriteLine("   {0}: {1}", ctr, sc.Read(ctr));

        }
    }
    // The example displays the following output:
    //    Task 1 read 0 items:
    //
    //    Task 3 wrote 17 items
    //
    //
    //    Task 1 read 17 items: [broccoli] [cauliflower] [carrot] [sorrel] [baby turnip] [
    //    beet] [brussel sprout] [cabbage] [plantain] [spinach] [grape leaves] [lime leave
    //    s] [corn] [radish] [cucumber] [raddichio] [lima beans]
    //
    //    Task 2 read 0 items:
    //
    //    Task 2 read 17 items: [lima beans] [raddichio] [cucumber] [radish] [corn] [lime
    //    leaves] [grape leaves] [spinach] [plantain] [cabbage] [brussel sprout] [beet] [b
    //    aby turnip] [sorrel] [carrot] [cauliflower] [broccoli]
    //
    //    Changed 'cucumber' to 'green bean'
    //
    //    Values in synchronized cache:
    //       1: broccoli
    //       2: cauliflower
    //       3: carrot
    //       4: sorrel
    //       5: baby turnip
    //       6: beet
    //       7: brussel sprout
    //       8: cabbage
    //       9: plantain
    //       10: spinach
    //       11: grape leaves
    //       12: lime leaves
    //       13: corn
    //       14: radish
    //       15: green bean
    //       16: raddichio
    //       17: lima beans
}