import crypto from "node:crypto";

const timestamp = "1778913640";
const body =
    '{"gateway":"ACB","transactionDate":"2026-05-16 13:19:03","accountNumber":"4518527","subAccount":null,"code":null,"content":"BE 2024hoaphonggmailcom FT26136806966362 GD 6136IBT1kCC3A3SD 160526-13:19:02","transferType":"in","description":"BankAPINotify BE 2024hoaphonggmailcom FT26136806966362 GD 6136IBT1kCC3A3SD 160526-13:19:02","transferAmount":990000,"referenceCode":"13019","accumulated":0,"id":58589721}';
const expected = "0d619e008a8d9a3f29932c2b213b104bdc01c35e95fc0279fc38b5c4e2981083";
const message = `${timestamp}.${body}`;

const secret = process.argv[2];
if (!secret) {
    console.error("Usage: node verify-sepay-hmac.mjs <SEPAY_WEBHOOK_SECRET>");
    process.exit(1);
}

const hex = crypto.createHmac("sha256", secret).update(message).digest("hex");
console.log("computed:", hex);
console.log("expected: ", expected);
console.log("match:   ", hex === expected);
