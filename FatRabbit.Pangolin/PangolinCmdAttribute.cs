using AspectCore.DynamicProxy;
using Polly;
using Polly.Timeout;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FatRabbit.Pangolin
{
    /// <summary>
    /// 穿山甲熔断降级aop框架
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class PangolinCmdAttribute : AbstractInterceptorAttribute
    {
        /// <summary>
        /// 是否允许熔断
        /// </summary>
        public bool EnableCircuitBreaker { get; set; }

        /// <summary>
        /// 熔断多长时间,单位毫秒
        /// </summary>
        public int DurationOfBreak { get; set; } = 3000;
        /// <summary>
        /// 标注的方法出现多少次错误后熔断
        /// </summary>
        public int ExceptionsAllowedBeforeBreaking { get; set; } = 3;


        /// <summary>
        /// 被标注的方法执行多少毫秒算超时，默认是1000毫秒,为0则是不执行此策略
        /// </summary>
        public int TimeOutMilliseconds { get; set; } = 1000;


        /// <summary>
        /// 每次重试的间隔时间
        /// </summary>
        public int RetryIntervalMilliseconds { get; set; } = 1000;

        /// <summary>
        /// 重试几次，如果为0则不重试，如果重试还是不行，那就执行降级方法
        /// </summary>
        public int RetryTimes { get; set; } = 0;

        /// <summary>
        /// 降级的方法，放在策略的最后执行
        /// </summary>
        private string _fallBackMethod { get; set; }

        /// <summary>
        /// 每个相同的方法创建一个实例，同时确保线程安全
        /// </summary>

        private static ConcurrentDictionary<MethodInfo, Policy> Policies = new ConcurrentDictionary<MethodInfo, Policy>();


        public PangolinCmdAttribute(string fallBackMethod)
        {
            _fallBackMethod = fallBackMethod;
        }

        public override async Task Invoke(AspectContext context, AspectDelegate next)
        {

            //取出来一个策略
            Policies.TryGetValue(context.ServiceMethod, out Policy policy);
            //防止多线程设置策略出现安全问题，先锁住它，等到策略赋值结束后再解锁
            lock (Policies)
            {
                //如果标注的这个方法上的策略是空的，就开始按照设定新建策略
                if (policy == null)
                {
                    policy = Policy.NoOpAsync();
                    //先进行外层熔断策略
                    if (EnableCircuitBreaker)
                    {
                       // Console.WriteLine("熔断毫秒数" + DurationOfBreak);
                        policy = policy.WrapAsync(Policy.Handle<Exception>().CircuitBreakerAsync(ExceptionsAllowedBeforeBreaking, TimeSpan.FromMilliseconds(DurationOfBreak)));
                    }
                  
                    //超时策略
                    if (TimeOutMilliseconds > 0)
                    {
                        //Console.WriteLine("超时" + TimeOutMilliseconds);
                        policy = policy.WrapAsync(Policy.TimeoutAsync(() => TimeSpan.FromMilliseconds(TimeOutMilliseconds), Polly.Timeout.TimeoutStrategy.Pessimistic));

                    }
                    //重试策略
                    if (RetryTimes > 0)
                    {
                       // Console.WriteLine("重试" + RetryTimes);
                        policy = policy.WrapAsync(Policy.Handle<Exception>().WaitAndRetryAsync(RetryTimes, p => TimeSpan.FromMilliseconds(RetryIntervalMilliseconds)));
                    }

                    //降级策略

                    Policy policyFallBackAsync = Policy.Handle<Exception>().FallbackAsync(async (ctx, c) => {
                       
                        //拿到降级方法
                        MethodInfo methodinfo = context.ServiceMethod.DeclaringType.GetMethod(_fallBackMethod);
                        //执行降级方法，并得到降级方法的返回值
                        Object fallBackResult = methodinfo.Invoke(context.Implementation, context.Parameters);//注意了降级方法的参数必须和标注的方法一致，否则就不知道给降级方法传递什么参数了
                        //让降级方法的返回值作为标注方法的返回值，这次不使用context.ReturnValue，因为如果被标注的方法已经有了策略后，再次执行的还是上次的context，我们不能取上次的context只能取当前这次的context
                        AspectContext aspectContext = (AspectContext)ctx["aspectContext"];
                        aspectContext.ReturnValue = fallBackResult;
                    }, async (ex, c) => {
                        //异常处理
                        //Console.WriteLine(ex.ToString());
                        }
                    );
                    
                    //降级策略处于最外层
                    policy = policyFallBackAsync.WrapAsync(policy);
                    //将方法和降级策略放入字典
                    Policies.TryAdd(context.ServiceMethod, policy);

                }

                

            }

            //临时把aspectcontext 的context存放到polly中，以供每一次策略执行时使用
            Polly.Context plyContext = new Polly.Context(); ;
            plyContext["aspectContext"] = context;

            //执行策略
            await policy.ExecuteAsync(p => next(context), plyContext);

            
        }
    }
}
