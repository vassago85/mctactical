using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HuntexPos.Api.Tests;

/// <summary>
/// Phase 3B.3 — Customer payment recording. Verifies allocation, partial payments,
/// overpayment surplus, and validation guards.
/// </summary>
public class CustomerPaymentTests
{
    private static (Customer customer, Invoice inv1, Invoice inv2) Seed(TestDb tdb, decimal creditLimit = 0m)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Email = "ar@example.com",
            Name = "AR Customer",
            AccountEnabled = true,
            CreditLimit = creditLimit,
            PaymentTermsDays = 30
        };
        var inv1 = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = "INV-1",
            Status = InvoiceStatus.Final,
            CustomerId = customer.Id,
            CustomerEmail = customer.Email,
            CustomerName = customer.Name,
            PaymentMethod = "Account",
            SubTotal = 1000m,
            GrandTotal = 1000m,
            AmountPaid = 0m,
            PaymentStatus = InvoicePaymentStatus.Unpaid,
            IsAccountSale = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        var inv2 = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = "INV-2",
            Status = InvoiceStatus.Final,
            CustomerId = customer.Id,
            CustomerEmail = customer.Email,
            CustomerName = customer.Name,
            PaymentMethod = "Account",
            SubTotal = 500m,
            GrandTotal = 500m,
            AmountPaid = 0m,
            PaymentStatus = InvoicePaymentStatus.Unpaid,
            IsAccountSale = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        tdb.Db.Customers.Add(customer);
        tdb.Db.Invoices.AddRange(inv1, inv2);
        tdb.Db.SaveChanges();
        return (customer, inv1, inv2);
    }

    [Fact]
    public async Task Create_FullPayment_ClosesInvoice()
    {
        using var tdb = new TestDb();
        var (customer, inv1, _) = Seed(tdb);

        var ctrl = ControllerFactory.MakeCustomerAccountsController(tdb);
        var result = await ctrl.Create(customer.Id, new CreateCustomerPaymentRequest(
            Amount: 1000m, Method: "Cash", Reference: null, Notes: null,
            PaidAt: null, ApplyToInvoiceIds: new[] { inv1.Id }), CancellationToken.None);

        var dto = ControllerFactory.Unwrap(result);
        Assert.Equal(0m, dto.UnallocatedCredit);

        // Re-read from a fresh context to verify persistence.
        using var verify = tdb.NewContext();
        var reloaded = await verify.Invoices.SingleAsync(i => i.Id == inv1.Id);
        Assert.Equal(1000m, reloaded.AmountPaid);
        Assert.Equal(InvoicePaymentStatus.Paid, reloaded.PaymentStatus);

        var balanceRows = await verify.Invoices
            .Where(i => i.CustomerId == customer.Id
                        && i.PaymentStatus != InvoicePaymentStatus.Paid
                        && i.PaymentStatus != InvoicePaymentStatus.WrittenOff)
            .Select(i => new { i.GrandTotal, i.AmountPaid })
            .ToListAsync();
        Assert.Equal(500m, balanceRows.Sum(i => i.GrandTotal - i.AmountPaid)); // inv2 still open
    }

    [Fact]
    public async Task Create_PartialPayment_MarksInvoicePartialAndKeepsBalance()
    {
        using var tdb = new TestDb();
        var (customer, inv1, _) = Seed(tdb);

        var ctrl = ControllerFactory.MakeCustomerAccountsController(tdb);
        var result = await ctrl.Create(customer.Id, new CreateCustomerPaymentRequest(
            Amount: 400m, Method: "EFT", Reference: "ref-123", Notes: null,
            PaidAt: null, ApplyToInvoiceIds: new[] { inv1.Id }), CancellationToken.None);

        var dto = ControllerFactory.Unwrap(result);
        Assert.Equal(0m, dto.UnallocatedCredit);

        using var verify = tdb.NewContext();
        var reloaded = await verify.Invoices.SingleAsync(i => i.Id == inv1.Id);
        Assert.Equal(400m, reloaded.AmountPaid);
        Assert.Equal(InvoicePaymentStatus.Partial, reloaded.PaymentStatus);

        var payments = await verify.CustomerPayments.Where(p => p.CustomerId == customer.Id).ToListAsync();
        Assert.Single(payments);
        Assert.Equal("EFT", payments[0].Method);
        Assert.Equal("ref-123", payments[0].Reference);
        Assert.Equal(inv1.Id, payments[0].InvoiceId);
    }

    [Fact]
    public async Task Create_AutoAllocate_AppliesOldestFirstAcrossMultipleInvoices()
    {
        using var tdb = new TestDb();
        var (customer, inv1, inv2) = Seed(tdb);
        // inv1 GrandTotal=1000 (older), inv2 GrandTotal=500 (newer). Pay 1200 → closes inv1, partial inv2 (200).

        var ctrl = ControllerFactory.MakeCustomerAccountsController(tdb);
        var result = await ctrl.Create(customer.Id, new CreateCustomerPaymentRequest(
            Amount: 1200m, Method: "Card", Reference: null, Notes: null,
            PaidAt: null, ApplyToInvoiceIds: null), CancellationToken.None);

        var dto = ControllerFactory.Unwrap(result);
        Assert.Equal(0m, dto.UnallocatedCredit);
        Assert.Equal(300m, dto.NewBalance); // 500 - 200 still owing on inv2

        using var verify = tdb.NewContext();
        var inv1Reloaded = await verify.Invoices.SingleAsync(i => i.Id == inv1.Id);
        var inv2Reloaded = await verify.Invoices.SingleAsync(i => i.Id == inv2.Id);
        Assert.Equal(1000m, inv1Reloaded.AmountPaid);
        Assert.Equal(InvoicePaymentStatus.Paid, inv1Reloaded.PaymentStatus);
        Assert.Equal(200m, inv2Reloaded.AmountPaid);
        Assert.Equal(InvoicePaymentStatus.Partial, inv2Reloaded.PaymentStatus);

        var payments = await verify.CustomerPayments.Where(p => p.CustomerId == customer.Id).ToListAsync();
        Assert.Equal(2, payments.Count); // one row per invoice the payment touched
    }

    [Fact]
    public async Task Create_Overpayment_CreatesUnallocatedCreditRow()
    {
        using var tdb = new TestDb();
        var (customer, inv1, inv2) = Seed(tdb);
        // Total open balance = 1500. Pay 2000 → 500 surplus.

        var ctrl = ControllerFactory.MakeCustomerAccountsController(tdb);
        var result = await ctrl.Create(customer.Id, new CreateCustomerPaymentRequest(
            Amount: 2000m, Method: "Cash", Reference: null, Notes: "Deposit",
            PaidAt: null, ApplyToInvoiceIds: null), CancellationToken.None);

        var dto = ControllerFactory.Unwrap(result);
        Assert.Equal(500m, dto.UnallocatedCredit);
        Assert.Equal(0m, dto.NewBalance);

        using var verify = tdb.NewContext();
        var payments = await verify.CustomerPayments.Where(p => p.CustomerId == customer.Id).ToListAsync();
        Assert.Equal(3, payments.Count); // 2 allocated + 1 surplus
        Assert.Single(payments, p => p.InvoiceId == null && p.Amount == 500m);
        Assert.Equal(InvoicePaymentStatus.Paid, (await verify.Invoices.SingleAsync(i => i.Id == inv1.Id)).PaymentStatus);
        Assert.Equal(InvoicePaymentStatus.Paid, (await verify.Invoices.SingleAsync(i => i.Id == inv2.Id)).PaymentStatus);
    }

    [Fact]
    public async Task Create_WithZeroAmount_ReturnsBadRequest()
    {
        using var tdb = new TestDb();
        var (customer, _, _) = Seed(tdb);
        var ctrl = ControllerFactory.MakeCustomerAccountsController(tdb);

        var result = await ctrl.Create(customer.Id, new CreateCustomerPaymentRequest(
            Amount: 0m, Method: "Cash", Reference: null, Notes: null,
            PaidAt: null, ApplyToInvoiceIds: null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ForCustomerWithoutAccountFlag_ReturnsBadRequest()
    {
        using var tdb = new TestDb();
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Email = "walkin@example.com",
            Name = "Walk-in",
            AccountEnabled = false
        };
        tdb.Db.Customers.Add(customer);
        tdb.Db.SaveChanges();

        var ctrl = ControllerFactory.MakeCustomerAccountsController(tdb);
        var result = await ctrl.Create(customer.Id, new CreateCustomerPaymentRequest(
            Amount: 100m, Method: "Cash", Reference: null, Notes: null,
            PaidAt: null, ApplyToInvoiceIds: null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_WithExplicitInvoiceFromAnotherCustomer_ReturnsBadRequest()
    {
        using var tdb = new TestDb();
        var (customer, _, _) = Seed(tdb);
        // Foreign invoice belonging to a different customer.
        var foreignInvoiceId = Guid.NewGuid();
        tdb.Db.Invoices.Add(new Invoice
        {
            Id = foreignInvoiceId,
            InvoiceNumber = "INV-X",
            Status = InvoiceStatus.Final,
            CustomerId = Guid.NewGuid(),
            PaymentMethod = "Account",
            SubTotal = 100m,
            GrandTotal = 100m,
            AmountPaid = 0m,
            PaymentStatus = InvoicePaymentStatus.Unpaid,
            IsAccountSale = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        tdb.Db.SaveChanges();

        var ctrl = ControllerFactory.MakeCustomerAccountsController(tdb);
        var result = await ctrl.Create(customer.Id, new CreateCustomerPaymentRequest(
            Amount: 50m, Method: "Cash", Reference: null, Notes: null,
            PaidAt: null, ApplyToInvoiceIds: new[] { foreignInvoiceId }), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
