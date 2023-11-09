FROM node:21-alpine as builder
WORKDIR /app

COPY package.json /app/
RUN npm install

COPY tsconfig.json /app/
COPY public/ /app/public
COPY src/ /app/src

RUN npm run build

FROM nginx:stable-alpine
COPY --from=builder /app/build /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]