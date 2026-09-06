using System.Xml.Linq;
using Klarfakt.Detection;
using Klarfakt.Model;

namespace Klarfakt.Reading;

internal sealed class UblInvoiceReader
{
    private readonly ReadContext _context = new();
    private readonly XElement _root;
    private readonly XName _lineName;
    private readonly XName _quantityName;
    private readonly XName _typeCodeName;

    internal UblInvoiceReader(XElement root, DocumentKind kind)
    {
        _root = root;
        var isCreditNote = kind == DocumentKind.CreditNote;
        _lineName = Ns.Cac + (isCreditNote ? "CreditNoteLine" : "InvoiceLine");
        _quantityName = Ns.Cbc + (isCreditNote ? "CreditedQuantity" : "InvoicedQuantity");
        _typeCodeName = Ns.Cbc + (isCreditNote ? "CreditNoteTypeCode" : "InvoiceTypeCode");
    }

    internal ReadResult Read()
    {
        var invoice = new EInvoice();

        ReadHeader(invoice);
        ReadParties(invoice);
        ReadDelivery(invoice);
        ReadPayment(invoice);
        ReadDocumentAllowancesAndCharges(invoice);
        ReadTaxBreakdown(invoice);
        ReadTotals(invoice);
        ReadAttachments(invoice);
        ReadLines(invoice);

        return new ReadResult(invoice, _context.Findings);
    }

    private void ReadHeader(EInvoice invoice)
    {
        invoice.SpecificationIdentifier = _root.El(Ns.Cbc + "CustomizationID").Text();
        invoice.BusinessProcess = _root.El(Ns.Cbc + "ProfileID").Text();
        invoice.Id = _root.El(Ns.Cbc + "ID").Text();
        invoice.IssueDate = _context.Date(_root.El(Ns.Cbc + "IssueDate"));
        invoice.DueDate = _context.Date(_root.El(Ns.Cbc + "DueDate"));
        invoice.TypeCode = _root.El(_typeCodeName).Text();
        invoice.CurrencyCode = _root.El(Ns.Cbc + "DocumentCurrencyCode").Text();
        invoice.TaxCurrencyCode = _root.El(Ns.Cbc + "TaxCurrencyCode").Text();
        invoice.TaxPointDate = _context.Date(_root.El(Ns.Cbc + "TaxPointDate"));
        invoice.BuyerReference = _root.El(Ns.Cbc + "BuyerReference").Text();
        invoice.AccountingCost = _root.El(Ns.Cbc + "AccountingCost").Text();

        foreach (var note in _root.Els(Ns.Cbc + "Note"))
        {
            invoice.Notes.Add(ParseNote(note.Text()));
        }

        invoice.InvoicePeriod = ReadPeriod(_root.El(Ns.Cac + "InvoicePeriod"));

        var order = _root.El(Ns.Cac + "OrderReference");
        invoice.PurchaseOrderReference = order.El(Ns.Cbc + "ID").Text();
        invoice.SalesOrderReference = order.El(Ns.Cbc + "SalesOrderID").Text();

        invoice.ContractReference = _root.Descend(Ns.Cac + "ContractDocumentReference", Ns.Cbc + "ID").Text();
        invoice.ProjectReference = _root.Descend(Ns.Cac + "ProjectReference", Ns.Cbc + "ID").Text();
        invoice.DespatchAdviceReference = _root.Descend(Ns.Cac + "DespatchDocumentReference", Ns.Cbc + "ID").Text();
        invoice.ReceivingAdviceReference = _root.Descend(Ns.Cac + "ReceiptDocumentReference", Ns.Cbc + "ID").Text();
        invoice.TenderReference = _root.Descend(Ns.Cac + "OriginatorDocumentReference", Ns.Cbc + "ID").Text();

        foreach (var reference in _root.Els(Ns.Cac + "BillingReference"))
        {
            var document = reference.El(Ns.Cac + "InvoiceDocumentReference");
            if (document is null) continue;

            invoice.PrecedingInvoices.Add(new PrecedingInvoiceReference
            {
                Id = document.El(Ns.Cbc + "ID").Text(),
                IssueDate = _context.Date(document.El(Ns.Cbc + "IssueDate")),
            });
        }
    }

    private void ReadParties(EInvoice invoice)
    {
        invoice.Seller = ReadParty(_root.Descend(Ns.Cac + "AccountingSupplierParty", Ns.Cac + "Party")) ?? new Party();
        invoice.Buyer = ReadParty(_root.Descend(Ns.Cac + "AccountingCustomerParty", Ns.Cac + "Party")) ?? new Party();
        invoice.Payee = ReadParty(_root.El(Ns.Cac + "PayeeParty"));
        invoice.SellerTaxRepresentative = ReadParty(_root.El(Ns.Cac + "TaxRepresentativeParty"));
    }

    private Party? ReadParty(XElement? element)
    {
        if (element is null) return null;

        var party = new Party
        {
            TradingName = element.Descend(Ns.Cac + "PartyName", Ns.Cbc + "Name").Text(),
            AdditionalLegalInformation = element.Descend(Ns.Cac + "PartyLegalEntity", Ns.Cbc + "CompanyLegalForm").Text(),
            Address = ReadAddress(element.El(Ns.Cac + "PostalAddress")),
        };

        var legalEntity = element.El(Ns.Cac + "PartyLegalEntity");
        party.Name = legalEntity.El(Ns.Cbc + "RegistrationName").Text() ?? party.TradingName;

        var companyId = legalEntity.El(Ns.Cbc + "CompanyID");
        if (companyId.Text() is { } legalId)
        {
            party.LegalRegistrationId = new Identifier(legalId, companyId.Attr("schemeID"));
        }

        var endpoint = element.El(Ns.Cbc + "EndpointID");
        if (endpoint.Text() is { } endpointValue)
        {
            party.ElectronicAddress = new Identifier(endpointValue, endpoint.Attr("schemeID"));
        }

        foreach (var identification in element.Els(Ns.Cac + "PartyIdentification"))
        {
            var id = identification.El(Ns.Cbc + "ID");
            if (id.Text() is { } idValue)
            {
                party.Identifiers.Add(new Identifier(idValue, id.Attr("schemeID")));
            }
        }

        foreach (var taxScheme in element.Els(Ns.Cac + "PartyTaxScheme"))
        {
            var companyIdentifier = taxScheme.El(Ns.Cbc + "CompanyID").Text();
            if (companyIdentifier is null) continue;

            var scheme = taxScheme.Descend(Ns.Cac + "TaxScheme", Ns.Cbc + "ID").Text();
            if (string.Equals(scheme, "VAT", StringComparison.OrdinalIgnoreCase))
            {
                party.VatIdentifier ??= companyIdentifier;
            }
            else
            {
                party.TaxRegistrationId ??= companyIdentifier;
            }
        }

        var contact = element.El(Ns.Cac + "Contact");
        if (contact is not null)
        {
            party.Contact = new Contact
            {
                Name = contact.El(Ns.Cbc + "Name").Text(),
                Phone = contact.El(Ns.Cbc + "Telephone").Text(),
                Email = contact.El(Ns.Cbc + "ElectronicMail").Text(),
            };
        }

        return party;
    }

    private static PostalAddress? ReadAddress(XElement? element)
    {
        if (element is null) return null;

        return new PostalAddress
        {
            Line1 = element.El(Ns.Cbc + "StreetName").Text(),
            Line2 = element.El(Ns.Cbc + "AdditionalStreetName").Text(),
            Line3 = element.Descend(Ns.Cac + "AddressLine", Ns.Cbc + "Line").Text(),
            City = element.El(Ns.Cbc + "CityName").Text(),
            PostalCode = element.El(Ns.Cbc + "PostalZone").Text(),
            CountrySubdivision = element.El(Ns.Cbc + "CountrySubentity").Text(),
            CountryCode = element.Descend(Ns.Cac + "Country", Ns.Cbc + "IdentificationCode").Text(),
        };
    }

    private void ReadDelivery(EInvoice invoice)
    {
        var element = _root.El(Ns.Cac + "Delivery");
        if (element is null) return;

        var delivery = new Delivery
        {
            ActualDeliveryDate = _context.Date(element.El(Ns.Cbc + "ActualDeliveryDate")),
            Name = element.Descend(Ns.Cac + "DeliveryParty", Ns.Cac + "PartyName", Ns.Cbc + "Name").Text(),
            Address = ReadAddress(element.Descend(Ns.Cac + "DeliveryLocation", Ns.Cac + "Address")),
        };

        var locationId = element.Descend(Ns.Cac + "DeliveryLocation", Ns.Cbc + "ID");
        if (locationId.Text() is { } location)
        {
            delivery.LocationIdentifier = new Identifier(location, locationId.Attr("schemeID"));
        }

        invoice.Delivery = delivery;
    }

    private void ReadPayment(EInvoice invoice)
    {
        var payment = invoice.Payment;
        payment.Terms = _root.Descend(Ns.Cac + "PaymentTerms", Ns.Cbc + "Note").Text();

        foreach (var means in _root.Els(Ns.Cac + "PaymentMeans"))
        {
            var code = means.El(Ns.Cbc + "PaymentMeansCode");
            payment.MeansCode ??= code.Text();
            payment.MeansText ??= code.Attr("name");
            payment.RemittanceInformation ??= means.El(Ns.Cbc + "PaymentID").Text();

            var account = means.El(Ns.Cac + "PayeeFinancialAccount");
            if (account is not null)
            {
                payment.CreditTransfers.Add(new CreditTransfer
                {
                    AccountIdentifier = account.El(Ns.Cbc + "ID").Text(),
                    AccountName = account.El(Ns.Cbc + "Name").Text(),
                    ServiceProviderIdentifier = account.Descend(Ns.Cac + "FinancialInstitutionBranch", Ns.Cbc + "ID").Text(),
                });
            }

            var card = means.El(Ns.Cac + "CardAccount");
            if (card is not null)
            {
                payment.Card ??= new PaymentCard
                {
                    PrimaryAccountNumber = card.El(Ns.Cbc + "PrimaryAccountNumberID").Text(),
                    HolderName = card.El(Ns.Cbc + "HolderName").Text(),
                };
            }

            var mandate = means.El(Ns.Cac + "PaymentMandate");
            if (mandate is not null)
            {
                payment.DirectDebit ??= new DirectDebit
                {
                    MandateReference = mandate.El(Ns.Cbc + "ID").Text(),
                    DebitedAccountIdentifier = mandate.Descend(Ns.Cac + "PayerFinancialAccount", Ns.Cbc + "ID").Text(),
                };
            }
        }
    }

    private void ReadDocumentAllowancesAndCharges(EInvoice invoice)
    {
        foreach (var element in _root.Els(Ns.Cac + "AllowanceCharge"))
        {
            invoice.AllowancesAndCharges.Add(ReadAllowanceCharge(element));
        }
    }

    private AllowanceCharge ReadAllowanceCharge(XElement element) => new()
    {
        IsCharge = _context.Indicator(element.El(Ns.Cbc + "ChargeIndicator")) ?? false,
        Amount = _context.Decimal(element.El(Ns.Cbc + "Amount")),
        BaseAmount = _context.Decimal(element.El(Ns.Cbc + "BaseAmount")),
        Percentage = _context.Decimal(element.El(Ns.Cbc + "MultiplierFactorNumeric")),
        Reason = element.El(Ns.Cbc + "AllowanceChargeReason").Text(),
        ReasonCode = element.El(Ns.Cbc + "AllowanceChargeReasonCode").Text(),
        TaxCategoryCode = element.Descend(Ns.Cac + "TaxCategory", Ns.Cbc + "ID").Text(),
        TaxPercent = _context.Decimal(element.Descend(Ns.Cac + "TaxCategory", Ns.Cbc + "Percent")),
    };

    private void ReadTaxBreakdown(EInvoice invoice)
    {
        foreach (var taxTotal in _root.Els(Ns.Cac + "TaxTotal"))
        {
            var amount = taxTotal.El(Ns.Cbc + "TaxAmount");
            var subtotals = taxTotal.Els(Ns.Cac + "TaxSubtotal").ToList();

            if (subtotals.Count == 0)
            {
                var currency = amount.Attr("currencyID");
                if (currency is not null && invoice.CurrencyCode is not null &&
                    !string.Equals(currency, invoice.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                {
                    invoice.Totals.TaxTotalAmountInAccountingCurrency ??= _context.Decimal(amount);
                    continue;
                }

                invoice.Totals.TaxTotalAmount ??= _context.Decimal(amount);
                continue;
            }

            invoice.Totals.TaxTotalAmount ??= _context.Decimal(amount);

            foreach (var subtotal in subtotals)
            {
                var category = subtotal.El(Ns.Cac + "TaxCategory");
                invoice.TaxBreakdown.Add(new TaxBreakdownEntry
                {
                    TaxableAmount = _context.Decimal(subtotal.El(Ns.Cbc + "TaxableAmount")),
                    TaxAmount = _context.Decimal(subtotal.El(Ns.Cbc + "TaxAmount")),
                    CategoryCode = category.El(Ns.Cbc + "ID").Text(),
                    Percent = _context.Decimal(category.El(Ns.Cbc + "Percent")),
                    ExemptionReason = category.El(Ns.Cbc + "TaxExemptionReason").Text(),
                    ExemptionReasonCode = category.El(Ns.Cbc + "TaxExemptionReasonCode").Text(),
                });
            }
        }
    }

    private void ReadTotals(EInvoice invoice)
    {
        var element = _root.El(Ns.Cac + "LegalMonetaryTotal");
        if (element is null) return;

        var totals = invoice.Totals;
        totals.LineExtensionAmount = _context.Decimal(element.El(Ns.Cbc + "LineExtensionAmount"));
        totals.TaxExclusiveAmount = _context.Decimal(element.El(Ns.Cbc + "TaxExclusiveAmount"));
        totals.TaxInclusiveAmount = _context.Decimal(element.El(Ns.Cbc + "TaxInclusiveAmount"));
        totals.AllowanceTotalAmount = _context.Decimal(element.El(Ns.Cbc + "AllowanceTotalAmount"));
        totals.ChargeTotalAmount = _context.Decimal(element.El(Ns.Cbc + "ChargeTotalAmount"));
        totals.PrepaidAmount = _context.Decimal(element.El(Ns.Cbc + "PrepaidAmount"));
        totals.RoundingAmount = _context.Decimal(element.El(Ns.Cbc + "PayableRoundingAmount"));
        totals.DuePayableAmount = _context.Decimal(element.El(Ns.Cbc + "PayableAmount"));
    }

    private void ReadAttachments(EInvoice invoice)
    {
        foreach (var reference in _root.Els(Ns.Cac + "AdditionalDocumentReference"))
        {
            var attachment = reference.El(Ns.Cac + "Attachment");
            var binary = attachment.El(Ns.Cbc + "EmbeddedDocumentBinaryObject");

            invoice.Attachments.Add(new Attachment
            {
                DocumentIdentifier = reference.El(Ns.Cbc + "ID").Text(),
                Description = reference.El(Ns.Cbc + "DocumentDescription").Text(),
                ExternalUri = attachment.Descend(Ns.Cac + "ExternalReference", Ns.Cbc + "URI").Text(),
                FileName = binary.Attr("filename"),
                MimeCode = binary.Attr("mimeCode"),
                Content = _context.Binary(binary),
            });
        }
    }

    private void ReadLines(EInvoice invoice)
    {
        foreach (var element in _root.Els(_lineName))
        {
            var quantity = element.El(_quantityName);
            var line = new InvoiceLine
            {
                Id = element.El(Ns.Cbc + "ID").Text(),
                Note = element.El(Ns.Cbc + "Note").Text(),
                Quantity = _context.Decimal(quantity),
                QuantityUnitCode = quantity.Attr("unitCode"),
                NetAmount = _context.Decimal(element.El(Ns.Cbc + "LineExtensionAmount")),
                AccountingCost = element.El(Ns.Cbc + "AccountingCost").Text(),
                OrderLineReference = element.Descend(Ns.Cac + "OrderLineReference", Ns.Cbc + "LineID").Text(),
                ObjectIdentifier = element.Descend(Ns.Cac + "DocumentReference", Ns.Cbc + "ID").Text(),
                Period = ReadPeriod(element.El(Ns.Cac + "InvoicePeriod")),
            };

            foreach (var allowanceCharge in element.Els(Ns.Cac + "AllowanceCharge"))
            {
                line.AllowancesAndCharges.Add(ReadAllowanceCharge(allowanceCharge));
            }

            ReadPrice(element.El(Ns.Cac + "Price"), line.Price);
            ReadItem(element.El(Ns.Cac + "Item"), line);

            invoice.Lines.Add(line);
        }
    }

    private void ReadPrice(XElement? element, LinePrice price)
    {
        if (element is null) return;

        var baseQuantity = element.El(Ns.Cbc + "BaseQuantity");
        price.NetAmount = _context.Decimal(element.El(Ns.Cbc + "PriceAmount"));
        price.BaseQuantity = _context.Decimal(baseQuantity);
        price.BaseQuantityUnitCode = baseQuantity.Attr("unitCode");

        var allowance = element.El(Ns.Cac + "AllowanceCharge");
        price.Discount = _context.Decimal(allowance.El(Ns.Cbc + "Amount"));
        price.GrossAmount = _context.Decimal(allowance.El(Ns.Cbc + "BaseAmount"));
    }

    private void ReadItem(XElement? element, InvoiceLine line)
    {
        if (element is null) return;

        var item = line.Item;
        item.Name = element.El(Ns.Cbc + "Name").Text();
        item.Description = element.El(Ns.Cbc + "Description").Text();
        item.SellerIdentifier = element.Descend(Ns.Cac + "SellersItemIdentification", Ns.Cbc + "ID").Text();
        item.BuyerIdentifier = element.Descend(Ns.Cac + "BuyersItemIdentification", Ns.Cbc + "ID").Text();
        item.OriginCountryCode = element.Descend(Ns.Cac + "OriginCountry", Ns.Cbc + "IdentificationCode").Text();

        var standardId = element.Descend(Ns.Cac + "StandardItemIdentification", Ns.Cbc + "ID");
        if (standardId.Text() is { } standard)
        {
            item.StandardIdentifier = new Identifier(standard, standardId.Attr("schemeID"));
        }

        foreach (var classification in element.Els(Ns.Cac + "CommodityClassification"))
        {
            var code = classification.El(Ns.Cbc + "ItemClassificationCode");
            if (code.Text() is { } codeValue)
            {
                item.ClassificationIdentifiers.Add(new Identifier(codeValue, code.Attr("listID")));
            }
        }

        foreach (var property in element.Els(Ns.Cac + "AdditionalItemProperty"))
        {
            item.Attributes.Add(new ItemAttribute
            {
                Name = property.El(Ns.Cbc + "Name").Text(),
                Value = property.El(Ns.Cbc + "Value").Text(),
            });
        }

        var taxCategory = element.El(Ns.Cac + "ClassifiedTaxCategory");
        line.TaxCategoryCode = taxCategory.El(Ns.Cbc + "ID").Text();
        line.TaxPercent = _context.Decimal(taxCategory.El(Ns.Cbc + "Percent"));
    }

    private DatePeriod? ReadPeriod(XElement? element)
    {
        if (element is null) return null;

        return new DatePeriod
        {
            StartDate = _context.Date(element.El(Ns.Cbc + "StartDate")),
            EndDate = _context.Date(element.El(Ns.Cbc + "EndDate")),
        };
    }

    private static InvoiceNote ParseNote(string? text)
    {
        if (text is null || text.Length < 3 || text[0] != '#')
        {
            return new InvoiceNote { Text = text };
        }

        var end = text.IndexOf('#', 1);
        if (end < 0)
        {
            return new InvoiceNote { Text = text };
        }

        return new InvoiceNote
        {
            SubjectCode = text[1..end],
            Text = text[(end + 1)..].TrimStart(),
        };
    }
}
