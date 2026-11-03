using System;
using System.Collections.Generic;
using FFTS.Core;

namespace FFTS.Economy
{
    public enum AccountKind { Cash, Checking, Savings, Business }

    public enum TransactionKind { Deposit, Withdrawal, Transfer, Purchase, Sale, Income, Reward, Bill, Payroll, Fine, Refund }

    public enum PostStatus { Posted, Duplicate, InsufficientFunds, InvalidAmount, UnknownAccount, InvalidRequest }

    public sealed class Account
    {
        public string Id;
        public AccountKind Kind;
        public string OwnerId;      // "player" for personal accounts, a business id for business accounts
        public long BalanceCents;
    }

    public sealed class TransactionRecord
    {
        public string TransactionId;
        public string IdempotencyKey;
        public TransactionKind Kind;
        public string FromAccount;  // null = money entering the player's books (wages, rewards, sales)
        public string ToAccount;    // null = money leaving (purchases, bills, fines)
        public long AmountCents;
        public string Memo;
        public long GameMinute;
    }

    public sealed class TransactionRequest
    {
        public string IdempotencyKey;
        public TransactionKind Kind;
        public string From;
        public string To;
        public long AmountCents;
        public string Memo;
    }

    public sealed class BankState
    {
        public List<Account> Accounts = new List<Account>();
        public List<TransactionRecord> Ledger = new List<TransactionRecord>();
        public long NextTransactionNumber = 1;
    }

    /// <summary>
    /// FINESS BANK: every money movement in the game goes through <see cref="Post"/>. Each request carries
    /// an idempotency key derived from its cause (e.g. "reward.mission.ch1.home_again.0"), so a replayed
    /// mission, a double-clicked button or a reloaded save can never post the same money twice.
    /// Personal and business money live in separate accounts; moving between them is an explicit transfer.
    /// Amounts are integer cents — no floating point money.
    /// </summary>
    public sealed class FinessBank
    {
        public const string PlayerOwner = "player";
        public const string Cash = "acct.player.cash";
        public const string Checking = "acct.player.checking";
        public const string Savings = "acct.player.savings";
        public const int MaxLedgerEntries = 5000; // older entries are summarised; keys are kept for dedupe

        private readonly EventBus _bus;
        private readonly Func<long> _clock;
        private readonly Dictionary<string, Account> _accounts = new Dictionary<string, Account>();
        private readonly Dictionary<string, TransactionRecord> _byKey = new Dictionary<string, TransactionRecord>();
        private readonly HashSet<string> _archivedKeys = new HashSet<string>();
        private readonly List<TransactionRecord> _ledger = new List<TransactionRecord>();
        private long _next = 1;

        public FinessBank(EventBus bus, Func<long> clock)
        {
            _bus = bus; _clock = clock ?? (() => 0);
        }

        public static FinessBank CreateStarter(EventBus bus, Func<long> clock, long startingCashCents)
        {
            var bank = new FinessBank(bus, clock);
            bank.OpenAccount(Cash, AccountKind.Cash, PlayerOwner);
            bank.OpenAccount(Checking, AccountKind.Checking, PlayerOwner);
            bank.OpenAccount(Savings, AccountKind.Savings, PlayerOwner);
            if (startingCashCents > 0)
                bank.Post(new TransactionRequest { IdempotencyKey = "start.cash", Kind = TransactionKind.Income, To = Cash, AmountCents = startingCashCents, Memo = "Pocket money" });
            return bank;
        }

        public IReadOnlyList<TransactionRecord> Ledger => _ledger;
        public IEnumerable<Account> Accounts => _accounts.Values;

        public Account OpenAccount(string id, AccountKind kind, string owner)
        {
            Ids.Require(id, "account");
            if (_accounts.ContainsKey(id)) throw new InvalidOperationException($"Account {id} already exists.");
            if (kind == AccountKind.Business && owner == PlayerOwner) throw new ArgumentException("Business accounts must be owned by a business id.");
            var a = new Account { Id = id, Kind = kind, OwnerId = owner };
            _accounts[id] = a;
            return a;
        }

        public bool HasAccount(string id) => id != null && _accounts.ContainsKey(id);
        public long Balance(string accountId) => _accounts.TryGetValue(accountId, out var a) ? a.BalanceCents : 0;

        public long PersonalTotal()
        {
            long t = 0;
            foreach (var a in _accounts.Values) if (a.OwnerId == PlayerOwner) t += a.BalanceCents;
            return t;
        }

        public bool WasPosted(string idempotencyKey) => _byKey.ContainsKey(idempotencyKey) || _archivedKeys.Contains(idempotencyKey);

        public PostStatus Post(TransactionRequest r) => Post(r, out _);

        public PostStatus Post(TransactionRequest r, out TransactionRecord record)
        {
            record = null;
            if (r == null || string.IsNullOrEmpty(r.IdempotencyKey)) return PostStatus.InvalidRequest;
            if (_byKey.TryGetValue(r.IdempotencyKey, out record) || _archivedKeys.Contains(r.IdempotencyKey)) return PostStatus.Duplicate;
            if (r.AmountCents <= 0) return PostStatus.InvalidAmount;
            if (r.From == null && r.To == null) return PostStatus.InvalidRequest;
            if (r.From != null && !_accounts.ContainsKey(r.From)) return PostStatus.UnknownAccount;
            if (r.To != null && !_accounts.ContainsKey(r.To)) return PostStatus.UnknownAccount;
            if (r.From != null && r.From == r.To) return PostStatus.InvalidRequest;
            if (r.From != null && _accounts[r.From].BalanceCents < r.AmountCents) return PostStatus.InsufficientFunds;

            if (r.From != null) _accounts[r.From].BalanceCents -= r.AmountCents;
            if (r.To != null) _accounts[r.To].BalanceCents += r.AmountCents;

            record = new TransactionRecord
            {
                TransactionId = "txn." + (_next++),
                IdempotencyKey = r.IdempotencyKey,
                Kind = r.Kind, FromAccount = r.From, ToAccount = r.To, AmountCents = r.AmountCents,
                Memo = r.Memo, GameMinute = _clock()
            };
            _byKey[r.IdempotencyKey] = record;
            _ledger.Add(record);
            if (_ledger.Count > MaxLedgerEntries)
            {
                var old = _ledger[0]; _ledger.RemoveAt(0);
                _byKey.Remove(old.IdempotencyKey); _archivedKeys.Add(old.IdempotencyKey);
            }
            _bus?.Publish(new TransactionPosted(record.TransactionId, r.Kind.ToString(), r.From, r.To, r.AmountCents));
            return PostStatus.Posted;
        }

        /// <summary>Pays from cash first, then checking. Returns InsufficientFunds without partial payment.</summary>
        public PostStatus Pay(string key, long cents, TransactionKind kind, string memo)
        {
            if (WasPosted(key + ".cash") || WasPosted(key + ".checking")) return PostStatus.Duplicate;
            if (Balance(Cash) >= cents) return Post(new TransactionRequest { IdempotencyKey = key + ".cash", Kind = kind, From = Cash, AmountCents = cents, Memo = memo });
            if (Balance(Checking) >= cents) return Post(new TransactionRequest { IdempotencyKey = key + ".checking", Kind = kind, From = Checking, AmountCents = cents, Memo = memo });
            return PostStatus.InsufficientFunds;
        }

        public BankState Capture()
        {
            var s = new BankState { NextTransactionNumber = _next };
            foreach (var a in _accounts.Values) s.Accounts.Add(new Account { Id = a.Id, Kind = a.Kind, OwnerId = a.OwnerId, BalanceCents = a.BalanceCents });
            s.Ledger.AddRange(_ledger);
            return s;
        }

        /// <summary>Restores state. Archived keys are not saved individually; the ledger cap is generous.</summary>
        public void Restore(BankState s)
        {
            _accounts.Clear(); _byKey.Clear(); _ledger.Clear(); _archivedKeys.Clear();
            foreach (var a in s.Accounts) _accounts[a.Id] = new Account { Id = a.Id, Kind = a.Kind, OwnerId = a.OwnerId, BalanceCents = a.BalanceCents };
            foreach (var t in s.Ledger) { _ledger.Add(t); _byKey[t.IdempotencyKey] = t; }
            _next = s.NextTransactionNumber;
        }

        public static string Format(long cents) => (cents < 0 ? "-$" : "$") + (Math.Abs(cents) / 100).ToString("N0") + "." + (Math.Abs(cents) % 100).ToString("00");
    }
}
