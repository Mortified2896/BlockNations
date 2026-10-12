// Match GridManager's standard 7x7 opening. Two-player layouts stay unchanged.
export function startingCities(seatCount, matchId) {
  let points = seatCount === 4 ? [[1,1],[5,5],[1,5],[5,1]] :
    seatCount === 3 ? [[3,1],[5,5],[1,5]] : [[1,1],[5,5]];
  if (seatCount === 3) {
    const seed = Number.parseInt(matchId.slice(0,8),16) >>> 0;
    for (let rotation = 0; rotation < seed % 4; rotation++) points = points.map(([x,y]) => [6-y,x]);
    const orders = [[0,1,2],[0,2,1],[1,0,2],[1,2,0],[2,0,1],[2,1,0]];
    points = orders[Math.floor(seed / 4) % 6].map(index => points[index]);
  }
  return points.map(([x,y],ownerSeatIndex) => ({ x,y,ownerSeatIndex }));
}

export function nextEligibleSequence(previous, snapshot) {
  const count = snapshot.seatCount;
  for (let step = 1; step <= count; step++) {
    const seat = (previous.currentTurnSeatIndex + step) % count;
    if (!["Resigned","Eliminated"].includes(snapshot.seats[seat].state)) return previous.transportSeq + step;
  }
  return -1;
}
