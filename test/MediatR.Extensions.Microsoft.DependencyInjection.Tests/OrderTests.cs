using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MediatR.Extensions.Microsoft.DependencyInjection.Tests
{

    public class OrderTests
    {
        public static string TestData { get; set; }

        [Fact]
        public void TestOrder()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddMediatR(typeof(OrderTests).Assembly);
            var mediator = serviceCollection.BuildServiceProvider().GetService<IMediator>();

            mediator.Publish(new EventData());

            Assert.Equal("CBA", TestData);
        }
    }

    public class EventData : INotification
    {

    }

    [EventOrder(3)]
    public class AHandler : INotificationHandler<EventData>
    {
        public Task Handle(EventData notification, CancellationToken cancellationToken)
        {
            OrderTests.TestData += "A";
            return Task.CompletedTask;
        }
    }

    [EventOrder(2)]
    public class BHandler : INotificationHandler<EventData>
    {
        public Task Handle(EventData notification, CancellationToken cancellationToken)
        {
            OrderTests.TestData += "B";
            return Task.CompletedTask;
        }
    }

    [EventOrder(1)]
    public class CHandler : INotificationHandler<EventData>
    {
        public Task Handle(EventData EventData, CancellationToken cancellationToken)
        {
            OrderTests.TestData += "C";
            return Task.CompletedTask;
        }
    }
}
