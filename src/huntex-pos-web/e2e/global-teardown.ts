import { execSync } from 'node:child_process'
import { API_CONTAINER, DATA_VOLUME } from './env'

// Windows doesn't deliver SIGTERM to the webServer process, so make sure the container goes.
export default function globalTeardown() {
  for (const cmd of [`docker rm -f ${API_CONTAINER}`, `docker volume rm -f ${DATA_VOLUME}`]) {
    try {
      execSync(cmd, { stdio: 'ignore' })
    } catch {
      /* already removed */
    }
  }
}
