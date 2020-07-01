using System;
using System.Collections.Generic;
using System.Text;

namespace MediatR.Extensions.Microsoft.DependencyInjection
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class EventOrderAttribute : Attribute
    {
        public EventOrderAttribute(int order)
        {
            Order = order;
        }

        public int Order { get; }
    }
}
