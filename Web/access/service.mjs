export { AccessStore } from "./store.mjs";

// Private service: no public URLs or routes. It exists only to own the durable
// admission/session namespace shared by production and preview game Workers.
export default { fetch() { return new Response(null, { status: 404 }); } };
