using AspectCore.DynamicProxy;
using FatRabbit.Pangolin;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FatRabbitTest
{
    class Program
    {
        static void Main(string[] args)
        {

            ProxyGeneratorBuilder proxyGeneratorBuilder = new ProxyGeneratorBuilder();
            Person person = proxyGeneratorBuilder.Build().CreateClassProxy<Person>();
            person.Say();

            Console.ReadKey();
        }
    }




    public class Person
    {
        [PangolinCmd(nameof(Hi), TimeOutMilliseconds = 1000, RetryTimes = 3, EnableCircuitBreaker = true)]
        public virtual async Task  Say()
        {
            //Thread.Sleep(5000);
           await  Task.Delay(5000);
          
            Console.WriteLine("老子会说话啊");
            throw new Exception("随便抛出一个异常");
           
            
        }
        [PangolinCmd(nameof(Nongsha),  RetryTimes = 3, EnableCircuitBreaker = true)]
        public virtual void Hi()
        {

            Console.WriteLine("老子降级了");
            throw new Exception();
           
        }

        public virtual void Nongsha()
        {
            Console.WriteLine("老子再次降级了");

            
        }



    }

}
