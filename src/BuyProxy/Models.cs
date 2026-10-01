// Generated from the BuyProxy API schema — do not edit by hand.
using System.Collections.Generic;

namespace BuyProxy.Models;

/// <summary>A product in the catalog.</summary>
public sealed record Product(string ProductKey, string ProductId, string Name, string Description, int MinGb, int? PricePerGbCents, string Currency);

/// <summary>Top-ups of thresholdGb or more get floor(gb × rate) extra GB free.</summary>
public sealed record VolumeBonus(int ThresholdGb, double Rate);

/// <summary>A subscription plan's price.</summary>
public sealed record PlanPrice(string Plan, int MonthlyPriceCents, double IncludedGb);

/// <summary>The public product catalog with live prices.</summary>
public sealed record Catalog(IReadOnlyList<Product> Products, VolumeBonus VolumeBonus, IReadOnlyList<PlanPrice> Plans);

/// <summary>The account behind the API key.</summary>
public sealed record Account(string Email, string Name, string Plan, string? PlanExpiresAt, double BalanceGb, string CreatedAt);

/// <summary>Spendable bandwidth on one product.</summary>
public sealed record Balance(string? ProductKey, string ProductId, string? PlanId, double ActiveGb, double PendingGb, double TotalGb, string? ExpiresAt);

/// <summary>Which targeting fields a plan supports.</summary>
public sealed record Targeting(bool Country, bool State, bool City, bool Isp);

/// <summary>What a plan's generate call accepts.</summary>
public sealed record GenerateCapabilities(int MaxCount, int MaxStickyMinutes, IReadOnlyList<string> Protocols, Targeting Targeting);

/// <summary>Bandwidth you hold on one product.</summary>
public sealed record Plan(string Id, string ProductId, string? ProductKey, string Name, string? Label, double DataGbRemaining, double DataGbTotal, string? ExpiresAt, string CreatedAt, GenerateCapabilities? Generate);

/// <summary>A Stripe checkout to open in a browser.</summary>
public sealed record CardCheckout(string PayUrl, string? TransactionId);

/// <summary>A crypto invoice to open in a browser.</summary>
public sealed record CryptoCheckout(string Url, string TransactionId);

/// <summary>One payment.</summary>
public sealed record Transaction(string Id, string Kind, string Status, string PaymentMethod, string? Interval, int AmountCents, string Currency, double? Gb, string? ProductKey, string? Plan, bool ResellerPool, string CreatedAt);

/// <summary>Unallocated reseller bandwidth on one product.</summary>
public sealed record PoolEntry(string ProductKey, string ProductId, string Label, double Gb);

/// <summary>Reseller account summary.</summary>
public sealed record ResellerSummary(string Email, int CustomerCount, int ActiveCustomerCount, IReadOnlyList<PoolEntry> Pool);

/// <summary>A reseller sub-account.</summary>
public sealed record Customer(string Username, string Label, string Status, string CreatedAt);

/// <summary>GB allocated but still being added to a plan.</summary>
public sealed record PendingAllocation(string ProductId, string? ProductKey, double Gb);

/// <summary>A plan plus the customer's remaining GB.</summary>
public sealed record CustomerPlan(string Id, string ProductId, string? ProductKey, string Name, string? Label, double DataGbRemaining, double DataGbTotal, string? ExpiresAt, string CreatedAt, GenerateCapabilities? Generate, double RemainingGb);

/// <summary>A customer with their plans.</summary>
public sealed record CustomerDetail(string Username, string Label, string Status, string CreatedAt, IReadOnlyList<CustomerPlan> Plans, IReadOnlyList<PendingAllocation> Pending);

/// <summary>Consumption on one plan.</summary>
public sealed record UsagePlan(string Id, string ProductId, string? ProductKey, double RemainingGb, double TotalGb, double UsedGb, string? ExpiresAt);

/// <summary>A snapshot of a customer's consumption.</summary>
public sealed record CustomerUsage(IReadOnlyList<UsagePlan> Plans, IReadOnlyList<PendingAllocation> Pending);

/// <summary>The result of an allocation.</summary>
public sealed record Allocation(string ProductKey, string ProductId, int Gb, bool AlreadyApplied, double PoolRemainingGb);

/// <summary>The result of signup.</summary>
public sealed record SignupResult(bool Ok, int? CooldownSeconds);

/// <summary>The verified user.</summary>
public sealed record VerifiedUser(string Email, string Name);

/// <summary>The result of OTP verification.</summary>
public sealed record VerifyResult(bool Ok, bool IsNew, VerifiedUser User);
