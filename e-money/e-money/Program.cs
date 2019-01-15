using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace e_money
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Press Q at the keyboard to quit.";
            DeviceCardReader dcr = new DeviceCardReader();
            while (true)
            {
                ConsoleKeyInfo result = Console.ReadKey(true);
                if (result.KeyChar == 'q' || result.KeyChar == 'Q' || result.Key == ConsoleKey.Escape)
                {
                    break;
                }
            }

            dcr.RunMain();
        }
    }
}
