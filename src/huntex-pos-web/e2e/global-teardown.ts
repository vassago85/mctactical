import { execSync } from 'node:child_process'
import { API_CONTAINER } from './env'

// Windows doesn't deliver SIGTERM to the webServer process, so make sure the container goes.
export default function globalTeardown() {
  try {
    execSync(`docker rm -f ${API_CONTAINER}`, { stdio: 'ignore' })
  } catch {
    /* already removed */
  }
}
