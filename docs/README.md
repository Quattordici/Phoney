# Phoney guides

The [main README](../README.md) is the reference: every feature, every option. These guides are the other way in, organised by what you are trying to do.

| Guide | Read it when |
|---|---|
| [Choosing a seed](seeds.md) | you want data that is the same every time, different every time but replayable, or tied to a customer id |
| [Cookbook](cookbook.md) | you have a concrete job: a database-ready aggregate, a validator to test, production data to mask, a fixture file |
| [Coming from Bogus](coming-from-bogus.md) | you know `Faker<T>` and `RuleFor` and want the Phoney way of saying it |
| [Questions and answers](faq.md) | something surprised you: a `null` member, a value that moved, a warning you didn't expect |

**Every Phoney example in these guides runs.** Each one is cut from a test in [`tests/Phoney.Tests/Docs`](../tests/Phoney.Tests/Docs), and the test suite fails when a guide shows other code than its test runs, so what you copy from here compiles against the current API and does what the text says.
