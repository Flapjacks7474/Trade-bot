# Economy, Business & Reputation Specification

## FINESS BANK — implemented (`FinessBank`)
- Accounts: Cash, Checking, Savings (player); Business accounts owned by business ids (personal ≠ business money).
- Every movement is a `TransactionRequest` with an **idempotency key**; duplicates return `Duplicate` and change nothing.
  Failed requests (insufficient funds, invalid amount, unknown account) don't consume the key.
- Integer cents only. Ledger persisted in saves (last 5,000 entries).
- `Pay` uses cash first, then checking.
- Not yet: loans/repayments, scheduled bills, payroll, interest.

## Reputation — implemented (`ReputationManager`)
Face Card and Public Image are independent 0–100 tracks plus Price on Head. Fight consequences by venue
(`FightConsequences`): an underground win raises Respect/Fear/Notoriety and lowers Professionalism; a sanctioned win
raises Popularity and Professionalism; a public street win raises Notoriety and Fear while lowering Credibility and
Professionalism.

## Businesses & property — NOT IMPLEMENTED (Milestone 9)
Categories: entertainment, automotive, retail/hospitality, fitness, property/investment. Attributes: reputation, demand,
employee quality, expenses, location, competition, equipment, security, marketing, management, loyalty, influence.
Employees have skill, experience, reliability, personality, loyalty, ambition, salary expectations, satisfaction, and can
quit, request promotion or compete. Ranks: Small Operator → Empire Builder. Weekly simulation posts revenue and expenses
through business accounts with period-keyed idempotency (`biz.<id>.week.<n>.revenue`).

**Design rule:** reward planning and avoid meaningless bills. Recurring costs are batched weekly, with clear summaries in
the Bank app.
