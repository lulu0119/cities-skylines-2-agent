using System;
using System.Collections.Generic;
using Game.City;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2MCP
{
    /// <summary>
    /// Management endpoints: loans (borrow/repay) and service fees
    /// (electricity/water/education... pricing).
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private BridgeResponse GetLoan()
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            LoanSystem loans = World.GetOrCreateSystemManaged<LoanSystem>();
            LoanInfo current = loans.CurrentLoan;
            return BridgeResponse.Json(new
            {
                currentLoan = new
                {
                    amount = current.m_Amount,
                    dailyInterestRate = current.m_DailyInterestRate,
                    dailyPayment = current.m_DailyPayment,
                },
                creditworthiness = loans.Creditworthiness,
                note = "set the loan principal with /city/loan/set?amount=N (0 repays fully, max = creditworthiness)",
            });
        }

        private BridgeResponse SetLoan(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetInt("amount", out int amount))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?amount=<int> (new loan principal; 0 repays fully)");
            }
            LoanSystem loans = World.GetOrCreateSystemManaged<LoanSystem>();
            int applied = math.clamp(amount, 0, loans.Creditworthiness);
            LoanInfo offer = loans.RequestLoanOffer(applied);
            loans.ChangeLoan(applied);
            LoanInfo current = loans.CurrentLoan;
            return BridgeResponse.Json(new
            {
                requestedAmount = amount,
                appliedAmount = applied,
                offer = new { offer.m_Amount, offer.m_DailyInterestRate, offer.m_DailyPayment },
                currentLoan = new { current.m_Amount, current.m_DailyInterestRate, current.m_DailyPayment },
            });
        }

        private BridgeResponse GetFees()
        {
            if (!TryGetCity(out Entity city, out BridgeResponse error))
            {
                return error;
            }
            ServiceFeeSystem feeSystem = World.GetOrCreateSystemManaged<ServiceFeeSystem>();
            DynamicBuffer<ServiceFee> fees = EntityManager.GetBuffer<ServiceFee>(city, isReadOnly: true);
            var result = new Dictionary<string, object>();
            foreach (PlayerResource resource in Enum.GetValues(typeof(PlayerResource)))
            {
                if ((int)resource < 0)
                {
                    continue;
                }
                if (ServiceFeeSystem.TryGetFee(resource, fees, out float fee))
                {
                    int3 limits = feeSystem.GetServiceFees(resource);
                    result[resource.ToString()] = new
                    {
                        fee,
                        estimatedMonthlyIncome = feeSystem.GetServiceFeeIncomeEstimate(resource, fee),
                        sliderRange = new { min = limits.x, max = limits.y, defaultValue = limits.z },
                    };
                }
            }
            return BridgeResponse.Json(new
            {
                note = "set with /city/fees/set?resource=<name>&fee=<float>; fees affect service income and citizen happiness",
                fees = result,
            });
        }

        private BridgeResponse SetFee(BridgeRequest request)
        {
            if (!TryGetCity(out Entity city, out BridgeResponse error))
            {
                return error;
            }
            if (!request.Query.TryGetValue("resource", out string resourceName)
                || !Enum.TryParse(resourceName, ignoreCase: true, out PlayerResource resource))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"provide ?resource=<{string.Join("|", Enum.GetNames(typeof(PlayerResource)))}>");
            }
            if (!request.TryGetFloat("fee", out float fee))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?fee=<float>");
            }
            DynamicBuffer<ServiceFee> fees = EntityManager.GetBuffer<ServiceFee>(city);
            if (!ServiceFeeSystem.TryGetFee(resource, fees, out float previous))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, $"resource '{resource}' has no adjustable fee in this city");
            }
            ServiceFeeSystem feeSystem = World.GetOrCreateSystemManaged<ServiceFeeSystem>();
            int3 limits = feeSystem.GetServiceFees(resource);
            if (fee < limits.x || fee > limits.y)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"fee {fee} out of slider range [{limits.x}, {limits.y}] for {resource}");
            }
            ServiceFeeSystem.SetFee(resource, fees, fee);
            return BridgeResponse.Json(new
            {
                resource = resource.ToString(),
                previousFee = previous,
                newFee = fee,
                sliderRange = new { min = limits.x, max = limits.y, defaultValue = limits.z },
            });
        }
    }
}
