# Functional law coverage

Run `dotnet test --project tests/Derail.Tests -c Release` from the repository root.
The named properties below live in the linked test classes. FsCheck supplies
inputs, including pure `Func` values for composition and fallible continuations.

| Primitive / test class | Laws and properties |
| --- | --- |
| [OptionProperties](PropertyTests.cs), [ResultProperties](PropertyTests.cs) | `Map_Identity`, `Map_Composition`; `Apply_Identity`, `Apply_Homomorphism`, `Apply_Interchange`, `Apply_Composition`; `Bind_LeftIdentity`, `Bind_RightIdentity`, `Bind_Associativity`. `ResultProperties.Recover_IsBindOnTheErrorSide` checks `Recover`. |
| [ValidationProperties](PropertyTests.cs) | Both `Map` laws and all four `Apply` laws above. `Zip_LeftUnit`, `Zip_RightUnit`, `Zip_Associativity`, `Zip_ConcatenatesErrorsInOrder`, `ThreeWayZip_MatchesNestedBinaryZip`, and `Sequence_MatchesZipFold` check accumulation. Validation has no `Bind`; `AndThen_AgreesWithBindThroughResult` pins its short-circuiting `AndThen` to `Result.Bind`, and `ToResult_ThenToValidationFromErrors_RoundTrips` checks the conversion. |
| [NonEmptyTests](NonEmptyTests.cs) | `Map_Identity`, `Map_Composition`, `Append_Associativity`, `Append_PreservesBothInputs`. There is no empty concatenation identity. |
| [AsyncProperties](AsyncProperties.cs) | `Result_BindAsync_*` and `Option_BindAsync_*` check left identity, right identity, and associativity for `Task` and `ValueTask` continuations. `Option_BindAsync_AgreesWithBind`, `Result_EnsureAsync_AgreesWithEnsure`, `Result_MapAndBind_AgreeAcrossSyncTaskAndValueTask` compare successful and unsuccessful pipelines. [AsyncExecutionTests](AsyncExecutionTests.cs) covers faults and cancellation separately. |

`OptionProperties.Bind_RightIdentity` includes explicit `None` and present nullable
payloads. Uninitialized union values are outside the laws: every operation that
inspects one throws `SwitchExpressionException` instead of producing a lawful value.
[OptionTests](OptionTests.cs), [ResultTests](ResultTests.cs) (`Default_IsRejectedByEveryMonadicOperation`),
and [ValidationTests](ValidationTests.cs) check this.
