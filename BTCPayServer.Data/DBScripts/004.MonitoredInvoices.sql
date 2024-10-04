CREATE OR REPLACE FUNCTION get_prompt(invoice_blob JSONB, payment_method_id TEXT)
RETURNS JSONB AS $$
    SELECT invoice_blob->'prompts'->payment_method_id
$$ LANGUAGE sql IMMUTABLE;


CREATE OR REPLACE FUNCTION get_monitored_invoices(arg_payment_method_id TEXT, include_non_activated BOOLEAN)
RETURNS TABLE (invoice_id TEXT, payment_id TEXT, payment_method_id TEXT) AS $$
WITH cte AS (
-- Get all the invoices which are pending. Even if no payments.
SELECT i."Id" invoice_id, p."Id" payment_id, p."PaymentMethodId" payment_method_id FROM "Invoices" i LEFT JOIN "Payments" p ON i."Id" = p."InvoiceDataId"
        WHERE is_pending(i."Status")
UNION ALL
-- For invoices not pending, take all of those which have pending payments
SELECT i."Id" invoice_id, p."Id" payment_id, p."PaymentMethodId" payment_method_id FROM "Invoices" i INNER JOIN "Payments" p ON i."Id" = p."InvoiceDataId"
        WHERE is_pending(p."Status") AND NOT is_pending(i."Status"))
SELECT cte.* FROM cte
JOIN "Invoices" i ON cte.invoice_id=i."Id"
LEFT JOIN "Payments" p ON cte.payment_id=p."Id" AND cte.payment_method_id=p."PaymentMethodId"
WHERE (p."PaymentMethodId" IS NOT NULL AND p."PaymentMethodId" = arg_payment_method_id) OR
      (p."PaymentMethodId" IS NULL AND get_prompt(i."Blob2", arg_payment_method_id) IS NOT NULL AND
        (include_non_activated IS TRUE OR (get_prompt(i."Blob2", arg_payment_method_id)->'inactive')::BOOLEAN IS NOT TRUE));
$$ LANGUAGE SQL STABLE;
