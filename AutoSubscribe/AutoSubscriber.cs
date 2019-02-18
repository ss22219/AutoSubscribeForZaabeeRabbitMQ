using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Zaabee.RabbitMQ.Abstractions;

namespace AutoSubscribe
{
    public class AutoSubscriber
    {
        private MethodInfo _subscribeMethod;
        private IZaabeeRabbitMqClient _rabbitMqClient;

        public AutoSubscriber(IZaabeeRabbitMqClient rabbitMqClient)
        {
            _rabbitMqClient = rabbitMqClient;
            var rabbitMqClientType = _rabbitMqClient.GetType();
            var methods = rabbitMqClientType.GetMethods();
            _subscribeMethod = methods.First(m =>
                m.Name == "SubscribeEvent" &&
                m.GetParameters()[0].Name == "exchange" &&
                m.GetParameters()[1].Name == "queue" &&
                m.GetParameters()[2].ParameterType.ContainsGenericParameters &&
                m.GetParameters()[2].ParameterType.GetGenericTypeDefinition() == typeof(Action<>));
        }
        public void AutoSubScribeEventHandler()
        {
            GetEventHandlerTypes().ForEach(
                eventHandlerType => SubScribeEventHandler(eventHandlerType)
            );
        }

        /// <summary>
        /// current assembly types
        /// </summary>
        private List<Type> GetEventHandlerTypes()
        {
            return typeof(AutoSubscriber).Assembly.GetTypes().Where(t =>
                t.Name.EndsWith("EventHandler")
                && !t.IsAbstract
                && !t.IsInterface
                && t.GetConstructors().Any(c => c.GetGenericArguments().Length == 0)
            ).ToList();
        }

        private void SubScribeEventHandler(Type eventHandlerType)
        {
            var instance = eventHandlerType.Assembly.CreateInstance(eventHandlerType.FullName);
            var handleMethods = GetHandleMethods(eventHandlerType);

            foreach (var handleMethod in handleMethods)
                SubscribeMethod(instance, handleMethod);

        }
        private List<MethodInfo> GetHandleMethods(Type eventHandlerType)
        {
            return eventHandlerType.GetMethods().Where(m => m.Name == "Handle" && m.GetParameters().Length == 1).ToList();
        }

        private void SubscribeMethod(object instance, MethodInfo handleMethod)
        {
            var paramTypeName = GetTypeName(handleMethod.DeclaringType);
            var exchangeName = paramTypeName;
            var queueName = GetQueueName(handleMethod, paramTypeName);
            var handleAction = GenerateHandleAction(instance, handleMethod);
            _subscribeMethod.MakeGenericMethod(handleMethod.GetParameters()[0].ParameterType)
                .Invoke(_rabbitMqClient,
                    new object[] { exchangeName, queueName, handleAction, (ushort)10 });
        }

        /// <summary>
        ///  Generate Action<T>
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="handleMethod"></param>
        /// <returns></returns>
        private object GenerateHandleAction(object instance, MethodInfo handleMethod)
        {
            var GenerateLambdaMethod = typeof(Expression).GetMethods().Where(m => m.Name == "Lambda" && m.IsGenericMethod).First().MakeGenericMethod(new Type[] { Expression.GetActionType(new Type[] { handleMethod.GetParameters()[0].ParameterType }) });

            var paramExpression = Expression.Parameter(handleMethod.GetParameters()[0].ParameterType);
            var callExpression = Expression.Call(Expression.Constant(instance), handleMethod, new Expression[] { paramExpression });

            // Expression<Action<T>> lambdaExpression = arg => Handle(arg)
            var lambdaExpression = GenerateLambdaMethod.Invoke(null, new object[] { callExpression, new ParameterExpression[]{ paramExpression } });

            //Compile to Action<T>
            return lambdaExpression.GetType().GetMethods().Where(m => m.Name == "Compile" && m.ReturnType != typeof(Delegate) && m.GetParameters().Length == 0).First().Invoke(lambdaExpression, new object[] { });
        }

        private string GetTypeName(Type type)
        {
            return type.ToString();
        }

        private string GetQueueName(MemberInfo memberInfo, string eventName)
        {
            return $"{memberInfo.ReflectedType?.FullName}.{memberInfo.Name}[{eventName}]";
        }
    }
}
