DROP FUNCTION get_monitored_invoices;
CREATE OR REPLACE FUNCTION get_monitored_invoices(payment_method_id TEXT, include_non_activated BOOLEAN)
RETURNS TABLE (invoice_id TEXT, payment_id TEXT, payment_method_id TEXT) AS $$
WITH cte AS (
-- Get all the invoices which are pending. Even if no payments.
SELECT i."Id" invoice_id, p."Id" payment_id, p."PaymentMethodId" payment_method_id FROM "Invoices" i LEFT JOIN "Payments" p ON i."Id" = p."InvoiceDataId"
        WHERE is_pending(i."Status")
UNION ALL
-- For invoices not pending, take all of those which have pending payments
SELECT i."Id", p."Id", p."PaymentMethodId" payment_method_id FROM "Invoices" i INNER JOIN "Payments" p ON i."Id" = p."InvoiceDataId"
        WHERE is_pending(p."Status") AND NOT is_pending(i."Status"))
SELECT cte.* FROM cte
LEFT JOIN "Payments" p ON cte.payment_id=p."Id" AND cte.payment_id=p."PaymentMethodId"
LEFT JOIN "Invoices" i ON cte.invoice_id=i."Id"
WHERE (p."PaymentMethodId" IS NOT NULL AND p."PaymentMethodId" = payment_method_id) OR
      (p."PaymentMethodId" IS NULL AND get_prompt(i."Blob2", payment_method_id) IS NOT NULL AND
        (include_non_activated IS TRUE OR (get_prompt(i."Blob2", payment_method_id)->'activated')::BOOLEAN IS NOT FALSE));
$$ LANGUAGE SQL STABLE;
