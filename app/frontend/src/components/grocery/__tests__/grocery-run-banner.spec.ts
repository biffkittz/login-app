import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import type { GroceryRun, GroceryRunParticipant } from '@/types/grocery'
import { makeRun, participant } from '@/utils/__tests__/test-runs'

import GroceryRunBanner from '../grocery-run-banner.vue'

function mountBanner(
  run: GroceryRun,
  {
    isShopper = false,
    me = null,
    secondsLeft = 25 * 60,
  }: { isShopper?: boolean; me?: GroceryRunParticipant | null; secondsLeft?: number } = {},
) {
  return mount(GroceryRunBanner, {
    props: { run, isShopper, myParticipation: me, secondsLeft },
  })
}

const buttons = (wrapper: ReturnType<typeof mountBanner>): string[] =>
  wrapper.findAll('button').map((button) => button.text())

describe('GroceryRunBanner', () => {
  it('asks someone who was asked for their "I\'m good", with the countdown', async () => {
    const ben = participant('ben', 'Ben')
    const wrapper = mountBanner(makeRun({ participants: [ben, participant('cy', 'Cy', true)] }), {
      me: ben,
      secondsLeft: 4 * 60 + 5,
    })

    expect(wrapper.get('h2').text()).toBe('Last call: Ada is going shopping')
    expect(wrapper.get('[data-run-timing]').text()).toBe('4:05 left')
    expect(wrapper.text()).toContain('Add anything you need now')
    expect(wrapper.text()).toContain('1 of 2 good to go')
    expect(buttons(wrapper)).toEqual(["I'm good"])

    await wrapper.get('button').trigger('click')
    expect(wrapper.emitted('action')).toEqual([['ready']])
  })

  it('thanks someone who is good to go, without a button', () => {
    const ben = participant('ben', 'Ben', true)
    const wrapper = mountBanner(makeRun({ participants: [ben] }), { me: ben })

    expect(wrapper.text()).toContain("You're good to go")
    expect(buttons(wrapper)).toEqual([])
  })

  it('lets someone who was not asked add things, without a button', () => {
    const wrapper = mountBanner(makeRun({ participants: [participant('ben', 'Ben')] }))

    expect(wrapper.text()).toContain('Add anything you need now')
    expect(buttons(wrapper)).toEqual([])
  })

  it('gives the shopper their buttons, and says when nobody else was asked', async () => {
    const wrapper = mountBanner(makeRun(), { isShopper: true })

    expect(wrapper.get('h2').text()).toBe("Last call: you're going shopping")
    expect(wrapper.text()).toContain('Nobody else has the list open')
    expect(buttons(wrapper)).toEqual(['Start shopping now', 'Call it off'])

    await wrapper.findAll('button')[1]!.trigger('click')
    expect(wrapper.emitted('action')).toEqual([['cancel']])
  })

  it('says it is wrapping up once the countdown has run out', () => {
    const wrapper = mountBanner(makeRun(), { secondsLeft: 0 })

    expect(wrapper.get('[data-run-timing]').text()).toBe('Wrapping up…')
  })

  it('shows the shopping, and gives the shopper the Done button', async () => {
    const run = makeRun({
      status: 'shopping',
      sequence: 3,
      shoppingStartedAt: new Date().toISOString(),
      lastCallOutcome: 'everyoneReady',
      participants: [participant('ben', 'Ben', true)],
    })

    const forBen = mountBanner(run)
    const forAda = mountBanner(run, { isShopper: true })

    expect(forBen.get('h2').text()).toBe('Ada is shopping')
    expect(forBen.text()).toContain('Everyone was good to go.')
    expect(forBen.findAll('li')).toHaveLength(0)
    expect(buttons(forBen)).toEqual([])
    expect(forAda.get('h2').text()).toBe("You're shopping")
    expect(buttons(forAda)).toEqual(['Done shopping'])
    await forAda.get('button').trigger('click')
    expect(forAda.emitted('action')).toEqual([['finish']])
  })
})
