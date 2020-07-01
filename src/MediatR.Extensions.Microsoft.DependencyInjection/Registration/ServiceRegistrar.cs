﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediatR.Extensions.Microsoft.DependencyInjection;
using MediatR.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediatR.Registration
{
    public static class ServiceRegistrar
    {
        public static void AddMediatRClasses(IServiceCollection services, IEnumerable<Assembly> assembliesToScan)
        {
            assembliesToScan = (assembliesToScan as Assembly[] ?? assembliesToScan).Distinct().ToArray();

            ConnectImplementationsToTypesClosing(typeof(IRequestHandler<,>), services, assembliesToScan, false);
            ConnectImplementationsToTypesClosing(typeof(INotificationHandler<>), services, assembliesToScan, true);
            ConnectImplementationsToTypesClosing(typeof(IRequestPreProcessor<>), services, assembliesToScan, true);
            ConnectImplementationsToTypesClosing(typeof(IRequestPostProcessor<,>), services, assembliesToScan, true);
            ConnectImplementationsToTypesClosing(typeof(IRequestExceptionHandler<,,>), services, assembliesToScan, true);
            ConnectImplementationsToTypesClosing(typeof(IRequestExceptionAction<,>), services, assembliesToScan, true);

            var multiOpenInterfaces = new[]
            {
                typeof(INotificationHandler<>),
                typeof(IRequestPreProcessor<>),
                typeof(IRequestPostProcessor<,>),
                typeof(IRequestExceptionHandler<,,>),
                typeof(IRequestExceptionAction<,>)
            };

            foreach (var multiOpenInterface in multiOpenInterfaces)
            {
                var concretions = assembliesToScan
                    .SelectMany(a => a.DefinedTypes)
                    .Where(type => type.FindInterfacesThatClose(multiOpenInterface).Any())
                    .Where(type => type.IsConcrete() && type.IsOpenGeneric())
                    .ToList();

                foreach (var type in concretions)
                {
                    services.AddTransient(multiOpenInterface, type);
                }
            }
        }

        /// <summary>
        /// Helper method use to differentiate behavior between request handlers and notification handlers.
        /// Request handlers should only be added once (so set addIfAlreadyExists to false)
        /// Notification handlers should all be added (set addIfAlreadyExists to true)
        /// </summary>
        /// <param name="openRequestInterface"></param>
        /// <param name="services"></param>
        /// <param name="assembliesToScan"></param>
        /// <param name="addIfAlreadyExists"></param>
        private static void ConnectImplementationsToTypesClosing(Type openRequestInterface,
            IServiceCollection services,
            IEnumerable<Assembly> assembliesToScan,
            bool addIfAlreadyExists)
        {
            // 这个集合收集中保存的是处理者。
            var concretions = new List<Type>();

            // 新增一个用于收集排序的集合。
            var concretionsOrder = new List<Type>();

            // 这个集合保存的是处理者的接口。
            var interfaces = new List<Type>();

            foreach (var type in assembliesToScan.SelectMany(a => a.DefinedTypes).Where(t => !t.IsOpenGeneric()))
            {
                var interfaceTypes = type.FindInterfacesThatClose(openRequestInterface).ToArray();
                if (!interfaceTypes.Any()) continue;

                if (type.IsConcrete())
                {
                    // 分离。
                    if (type.GetCustomAttributes().Any(o => o.GetType().Equals(typeof(EventOrderAttribute))))
                        concretionsOrder.Add(type);
                    else
                        concretions.Add(type);
                }

                foreach (var interfaceType in interfaceTypes)
                {
                    interfaces.Fill(interfaceType);
                }
            }

            // 排序后加入到服务中。
            concretionsOrder = OrderService(concretionsOrder);
            AddService(concretions);
            AddService(concretionsOrder);

            // 排序服务。
            List<Type> OrderService(List<Type> concretions)
            {
                if (concretions.Any())
                    return concretions.OrderBy(o => o.GetCustomAttribute<EventOrderAttribute>().Order).ToList();
                else
                    return concretions;
            }

            // 添加服务。
            void AddService(List<Type> concretions)
            {
                // 这一部分是将接口与相应的实例者相匹配，然后注册到容器中。
                foreach (var @interface in interfaces)
                {
                    var exactMatches = concretions.Where(x => x.CanBeCastTo(@interface)).ToList();
                    if (addIfAlreadyExists)
                    {
                        foreach (var type in exactMatches)
                        {
                            services.AddTransient(@interface, type);
                        }
                    }
                    else
                    {
                        if (exactMatches.Count > 1)
                        {
                            exactMatches.RemoveAll(m => !IsMatchingWithInterface(m, @interface));
                        }

                        foreach (var type in exactMatches)
                        {
                            services.TryAddTransient(@interface, type);
                        }
                    }

                    if (!@interface.IsOpenGeneric())
                    {
                        AddConcretionsThatCouldBeClosed(@interface, concretions, services);
                    }
                }
            }
        }

        private static bool IsMatchingWithInterface(Type handlerType, Type handlerInterface)
        {
            if (handlerType == null || handlerInterface == null)
            {
                return false;
            }

            if (handlerType.IsInterface)
            {
                if (handlerType.GenericTypeArguments.SequenceEqual(handlerInterface.GenericTypeArguments))
                {
                    return true;
                }
            }
            else
            {
                return IsMatchingWithInterface(handlerType.GetInterface(handlerInterface.Name), handlerInterface);
            }

            return false;
        }

        private static void AddConcretionsThatCouldBeClosed(Type @interface, List<Type> concretions, IServiceCollection services)
        {
            foreach (var type in concretions
                .Where(x => x.IsOpenGeneric() && x.CouldCloseTo(@interface)))
            {
                try
                {
                    services.TryAddTransient(@interface, type.MakeGenericType(@interface.GenericTypeArguments));
                }
                catch (Exception)
                {
                }
            }
        }

        private static bool CouldCloseTo(this Type openConcretion, Type closedInterface)
        {
            var openInterface = closedInterface.GetGenericTypeDefinition();
            var arguments = closedInterface.GenericTypeArguments;

            var concreteArguments = openConcretion.GenericTypeArguments;
            return arguments.Length == concreteArguments.Length && openConcretion.CanBeCastTo(openInterface);
        }

        private static bool CanBeCastTo(this Type pluggedType, Type pluginType)
        {
            if (pluggedType == null) return false;

            if (pluggedType == pluginType) return true;

            return pluginType.GetTypeInfo().IsAssignableFrom(pluggedType.GetTypeInfo());
        }

        public static bool IsOpenGeneric(this Type type)
        {
            return type.GetTypeInfo().IsGenericTypeDefinition || type.GetTypeInfo().ContainsGenericParameters;
        }

        public static IEnumerable<Type> FindInterfacesThatClose(this Type pluggedType, Type templateType)
        {
            return FindInterfacesThatClosesCore(pluggedType, templateType).Distinct();
        }

        private static IEnumerable<Type> FindInterfacesThatClosesCore(Type pluggedType, Type templateType)
        {
            if (pluggedType == null) yield break;

            if (!pluggedType.IsConcrete()) yield break;

            if (templateType.GetTypeInfo().IsInterface)
            {
                foreach (
                    var interfaceType in
                    pluggedType.GetInterfaces()
                        .Where(type => type.GetTypeInfo().IsGenericType && (type.GetGenericTypeDefinition() == templateType)))
                {
                    yield return interfaceType;
                }
            }
            else if (pluggedType.GetTypeInfo().BaseType.GetTypeInfo().IsGenericType &&
                     (pluggedType.GetTypeInfo().BaseType.GetGenericTypeDefinition() == templateType))
            {
                yield return pluggedType.GetTypeInfo().BaseType;
            }

            if (pluggedType.GetTypeInfo().BaseType == typeof(object)) yield break;

            foreach (var interfaceType in FindInterfacesThatClosesCore(pluggedType.GetTypeInfo().BaseType, templateType))
            {
                yield return interfaceType;
            }
        }

        private static bool IsConcrete(this Type type)
        {
            return !type.GetTypeInfo().IsAbstract && !type.GetTypeInfo().IsInterface;
        }

        private static void Fill<T>(this IList<T> list, T value)
        {
            if (list.Contains(value)) return;
            list.Add(value);
        }

        public static void AddRequiredServices(IServiceCollection services, MediatRServiceConfiguration serviceConfiguration)
        {
            // Use TryAdd, so any existing ServiceFactory/IMediator registration doesn't get overriden
            services.TryAddTransient<ServiceFactory>(p => p.GetService);
            services.TryAdd(new ServiceDescriptor(typeof(IMediator), serviceConfiguration.MediatorImplementationType, serviceConfiguration.Lifetime));

            // Use TryAddTransientExact (see below), we dó want to register our Pre/Post processor behavior, even if (a more concrete)
            // registration for IPipelineBehavior<,> already exists. But only once.
            services.TryAddTransientExact(typeof(IPipelineBehavior<,>), typeof(RequestPreProcessorBehavior<,>));
            services.TryAddTransientExact(typeof(IPipelineBehavior<,>), typeof(RequestPostProcessorBehavior<,>));
            services.TryAddTransientExact(typeof(IPipelineBehavior<,>), typeof(RequestExceptionActionProcessorBehavior<,>));
            services.TryAddTransientExact(typeof(IPipelineBehavior<,>), typeof(RequestExceptionProcessorBehavior<,>));
        }

        /// <summary>
        /// Adds a new transient registration to the service collection only when no existing registration of the same service type and implementation type exists.
        /// In contrast to TryAddTransient, which only checks the service type.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="serviceType">Service type</param>
        /// <param name="implementationType">Implementation type</param>
        private static void TryAddTransientExact(this IServiceCollection services, Type serviceType, Type implementationType)
        {
            if (services.Any(reg => reg.ServiceType == serviceType && reg.ImplementationType == implementationType))
            {
                return;
            }

            services.AddTransient(serviceType, implementationType);
        }
    }
}