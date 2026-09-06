# Pricing

Most people reading this owe nothing. **Verifacta is free unless your organisation
turns over more than €1,000,000 a year**, free at any size inside OSI-licensed
open source, and free always for evaluation, development, CI and internal testing.

If that covers you, stop here and go back to the [README](../README.md).

## Commercial licence

For production use above that threshold. Per organisation per year, excluding VAT
where applicable.

| | **Standard** | **Extended** |
|---|---|---|
| | **€690** / year | **€1,900** / year |
| Developers | unlimited | unlimited |
| Deployments | unlimited | unlimited |
| Validations | unlimited | unlimited |
| Rule pack updates | for the term | for the term |
| Support | email, usually within two working days | one working day, Monday to Friday |
| | | A named contact, and your issues looked at ahead of the backlog |
| Suited to | using Verifacta inside your own systems | shipping it inside a product you sell |

**No metering, ever.** Validation happens in your process, on your hardware.
Verifacta cannot count your invoices and is not built to.

### What a licence keeps

A licence covers every version released during its term, **permanently**. Licence
today, stop renewing in a year, and you keep the right to run everything
published in that year, in production, indefinitely. What lapses is new versions
and support, not the software already in your pipeline.

That matters more than usual here, because Verifacta is maintained by one person.
You should not have to bet an invoice pipeline on anyone's continued attention.

### Founding customers

The first three commercial licences are **€390 a year, held at that price for
three years**, in exchange for permission to name the customer and an honest
conversation about what is missing. Three people saying what is wrong with this
is worth more right now than ten silent renewals.

## Rule packs

This is the part being paid for. CEN, OpenPEPPOL and KoSIT each publish roughly
twice a year, and a national CIUS release can change the verdict on invoices that
passed last month.

The commitment: **a new pack version within 30 days of its upstream release**,
with a report of every corpus verdict that changed. Bumps are never silent —
`tools/reference-diff.py` compares every verdict against KoSIT's own validator on
each run and the build fails on a disagreement that has not been written down.

## Supported frameworks

Every .NET version Microsoft still supports, plus the current LTS. Today that is
**net8.0 and net10.0**, both tested on every commit. A framework is dropped only
after Microsoft ends support for it, announced one minor version in advance.

## Buying

Email **oleganickij02@gmail.com** with the organisation name and the tier. Back
comes an invoice and a licence certificate naming the organisation. No sales call,
no procurement portal, no per-seat audit.

Unsure whether the threshold applies, or whether a particular use counts as
production? Ask. A straight answer costs one email.

## If the project stops

**Every version of Verifacta becomes Apache 2.0 four years after it is
published.** The [licence](../LICENSE) sets that as a ceiling and it cannot be
extended or revoked — version 1.x converts on 2030-09-04, and later versions
carry their own dates.

So the worst case for a customer is ending up with permanently free open-source
software, on a schedule that is written down rather than promised.
