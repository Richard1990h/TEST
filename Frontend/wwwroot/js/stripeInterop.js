window.redirectToStripeCheckout = redirectToStripeCheckout;

export async function redirectToStripeCheckout(data) {
    console.log("🟦 redirectToStripeCheckout called!", data);

    if (!window.Stripe) {
        alert('❌ Stripe.js not loaded!');
        return;
    }

    const stripe = window.Stripe('pk_live_51REhtTEpmkCkAObmjWncBTinQ1nAHnZQFamrctaXnZp0SzYUikPxU3bo91x4dllxgGpHroXkS0qHUr5cxkClC4xP00zeZqJb6g');

    if (!data.sessionId) {
        alert('❌ No sessionId provided!');
        return;
    }

    console.log('🟦 Redirecting to checkout with sessionId:', data.sessionId);
    await stripe.redirectToCheckout({ sessionId: data.sessionId });
}

