using MassTransit;
using SeatGrid.PaymentService;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMassTransit(x =>
{
    // Register the payment consumer so each BookingInitiated message creates a scoped instance
    x.AddConsumer<PaymentConsumer>();

    // Use RabbitMQ transport and auto-configure receive endpoints for the registered consumer
    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqHost = builder.Configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost";
        cfg.Host(rabbitMqHost, "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // Declares the queue/bindings for PaymentConsumer to receive BookingInitiated events
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
