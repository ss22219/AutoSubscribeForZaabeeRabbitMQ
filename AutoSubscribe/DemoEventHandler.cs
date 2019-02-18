using System;

namespace AutoSubscribe
{
    public class DemoEventHandler
    {
        void Handle(string arg)
        {
            Console.WriteLine(arg);
        }

        void Handle(int arg)
        {
            Console.WriteLine(arg);
        }

        void Handle(long arg)
        {
            Console.WriteLine(arg);
        }
    }
}
