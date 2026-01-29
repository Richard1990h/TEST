const canvas = document.getElementById('game-canvas');
const ctx = canvas.getContext('2d');

// Game objects
let snake = [];
let food = { x: 10, y: 10 };
let score = 0;
let gameOver = false;

// Setup
canvas.width = 300;
canvas.height = 300;

// Render
function render() {
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = '#000';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = '#fff';
  ctx.font = '24px Arial';
  ctx.textAlign = 'center';
  ctx.fillText('Score: ' + score, canvas.width / 2, 30);
  if (gameOver) {
    ctx.fillStyle = '#f00';
    ctx.font = '48px Arial';
    ctx.fillText('Game Over', canvas.width / 2, canvas.height / 2);
  } else {
    snake.forEach(({ x, y }) => ctx.fillRect(x * 10, y * 10, 10, 10));
    ctx.fillStyle = '#0f0';
    ctx.fillRect(food.x * 10, food.y * 10, 10, 10);
  }
}

// Update
function update() {
  if (gameOver) return;
  const snakeX = snake[0].x;
  const snakeY = snake[0].y;
  const foodX = food.x;
  const foodY = food.y;

  // Move snake
  if (snakeX < foodX) snakeX++;
  else if (snakeX > foodX) snakeX--;
  else if (snakeY < foodY) snakeY++;
  else if (snakeY > foodY) snakeY--;
  snake.unshift({ x: snakeX, y: snakeY });
  snake.pop();

  // Check for game over
  if (snakeX < 0 || snakeX > 29 || snakeY < 0 || snakeY > 29) gameOver = true;

  // Check for collision
  snake.forEach(({ x, y }, i) => {
    if (x === foodX && y === foodY && i !== 0) gameOver = true;
  });
}

// Game loop
function gameLoop() {
  update();
  render();
  requestAnimationFrame(gameLoop);
}

// Start game
gameLoop();

// Handle input
window.addEventListener('keydown', e => {
  if (e.key === 'ArrowUp') snake[0].y--;
  if (e.key === 'ArrowDown') snake[0].y++;
  if (e.key === 'ArrowLeft') snake[0].x--;
  if (e.key === 'ArrowRight') snake[0].x++;
});